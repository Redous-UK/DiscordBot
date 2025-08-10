using MyDiscordBot.Services;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyDiscordBot
{
    public static class Program
    {
        public static Bot BotInstance { get; private set; } = null!;
        public static ReminderService ReminderService { get; private set; } = null!;

        private static ConnectionMultiplexer? _mux;
        private static IDatabase? _db;
        private static string _lockKey = "mydiscordbot:leader";
        private static string _lockValue = Guid.NewGuid().ToString("n");
        private static CancellationTokenSource? _renewCts;

        public static async Task Main(string[] args)
        {
            // --- Visibility: prove if REDIS_URL arrived at runtime ---
            var rawUrl = Environment.GetEnvironmentVariable("REDIS_URL");
            Console.WriteLine($"[leader] REDIS_URL present? {!string.IsNullOrWhiteSpace(rawUrl)} len={(rawUrl?.Length ?? 0)}");

            // Acquire leader lock (non-fatal if REDIS_URL missing)
            var ttl = TimeSpan.FromSeconds(30);
            if (!await TryAcquireLeaderAsync(ttl))
            {
                Console.WriteLine("[leader] Another instance is active. Exiting.");
                return; // don't start the bot if we didn't acquire the lock
            }

            // Release lock on shutdown
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; OnProcessExit(null, EventArgs.Empty); };

            BotInstance = new Bot(ReminderService);
            await BotInstance.RunAsync();
        }

        private static string? GetRedisUrlFromEnv()
        {
            foreach (var k in new[] { "REDIS_URL", "UPSTASH_REDIS_URL", "REDIS", "REDIS_CONNECTION_STRING" })
            {
                var v = Environment.GetEnvironmentVariable(k);
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        private static async Task<bool> TryAcquireLeaderAsync(TimeSpan ttl)
        {
            try
            {
                var url = GetRedisUrlFromEnv();
                if (string.IsNullOrWhiteSpace(url))
                {
                    Console.WriteLine("[leader] No Redis URL found. Running WITHOUT leader lock.");
                    return true;
                }

                // Parse URI (handles rediss:// and user:pass@host:port)
                var options = ConfigurationOptions.Parse(url, ignoreUnknown: true);
                options.AbortOnConnectFail = false;   // keep retrying in background
                options.ConnectRetry = 5;
                options.ConnectTimeout = 10000;       // 10s
                options.KeepAlive = 15;               // seconds
                options.ResolveDns = true;

                // If you want to be explicit (usually not needed when using rediss://)
                if (options.Ssl)                      // true for rediss://
                {
                    // options.SslHost = options.EndPoints.First().ToString(); // optional
                }

                _mux = await ConnectionMultiplexer.ConnectAsync(options);
                _db = _mux.GetDatabase();

                _lockKey = Environment.GetEnvironmentVariable("LEADER_LOCK_KEY") ?? _lockKey;

                var instanceId = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID");
                if (!string.IsNullOrWhiteSpace(instanceId)) _lockValue = instanceId;

                // Acquire once (NX + TTL)
                var acquired = await _db.StringSetAsync(_lockKey, _lockValue, ttl, When.NotExists);
                var ep = _mux.GetEndPoints().FirstOrDefault();
                Console.WriteLine($"[leader] Lock '{_lockKey}' → {(acquired ? "acquired" : "already held")} | endpoint={ep}");

                if (!acquired) return false;

                _renewCts = new CancellationTokenSource();
                _ = Task.Run(() => RenewLoopAsync(ttl, _renewCts.Token));
                return true;
            }
            catch (Exception ex)
            {
                // Don’t crash the bot just because Redis hiccuped
                Console.WriteLine("[leader] Redis connect/lock error: " + ex.Message);
                return true; // set to 'false' if you prefer hard-fail when lock unavailable
            }
        }

        private static async Task RenewLoopAsync(TimeSpan ttl, CancellationToken ct)
        {
            var delay = ttl - TimeSpan.FromSeconds(5);
            if (delay < TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, ct);
                    if (_db == null) break;

                    // Renew TTL only if we still own the lock
                    var tran = _db.CreateTransaction();
                    tran.AddCondition(Condition.StringEqual(_lockKey, _lockValue));
                    _ = tran.KeyExpireAsync(_lockKey, ttl); // queue op under the condition

                    bool ok = await tran.ExecuteAsync();
                    if (!ok)
                    {
                        Console.WriteLine("[leader] Lost leader lock (renew failed); exiting.");
                        Environment.Exit(0);
                    }
                }
                catch (TaskCanceledException) { /* shutdown */ }
                catch (Exception ex)
                {
                    Console.WriteLine("[leader] Renew error: " + ex.Message);
                    // transient; will retry next tick
                }
            }
        }

        private static void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                _renewCts?.Cancel();

                if (_db != null)
                {
                    // Delete only if we still own the lock
                    var tran = _db.CreateTransaction();
                    tran.AddCondition(Condition.StringEqual(_lockKey, _lockValue));
                    _ = tran.KeyDeleteAsync(_lockKey);
                    _ = tran.Execute(); // sync is fine during process exit
                }

                _mux?.Dispose();
            }
            catch { /* best-effort cleanup */ }
        }
    }
}