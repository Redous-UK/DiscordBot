using MyDiscordBot.Services;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace MyDiscordBot
{
    public static class Program
    {
        public static Bot BotInstance { get; private set; }
        public static ReminderService ReminderService { get; private set; }  // ← add this

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
                    // No Redis configured: run without a lock (change to 'return false' to hard-require Redis)
                    Console.WriteLine("[leader] No Redis URL found. Running WITHOUT leader lock.");
                    return true;
                }

                _mux = await ConnectionMultiplexer.ConnectAsync(url);
                _db = _mux.GetDatabase();

                _lockKey = Environment.GetEnvironmentVariable("LEADER_LOCK_KEY") ?? _lockKey;

                var instanceId = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID");
                if (!string.IsNullOrWhiteSpace(instanceId))
                    _lockValue = instanceId;

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
                Console.WriteLine("[leader] Redis connect/lock error: " + ex.Message);
                // Choose your policy:
                return true; // keep running without lock; set to 'false' to abort on failure
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