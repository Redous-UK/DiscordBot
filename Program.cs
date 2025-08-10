using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using StackExchange.Redis;
using MyDiscordBot.Services;

namespace MyDiscordBot
{
    public static class Program
    {
        public static Bot BotInstance { get; private set; } = null!;
        public static ReminderService ReminderService { get; private set; } = null!;

        // --- Redis leader lock fields ---
        private static ConnectionMultiplexer? _mux;
        private static IDatabase? _db;
        private static string _lockKey = "mydiscordbot:leader";
        private static string _lockValue = Guid.NewGuid().ToString("n");
        private static CancellationTokenSource? _renewCts;

        public static async Task Main(string[] args)
        {
            // Visibility: prove env var arrived
            var rawUrl = Environment.GetEnvironmentVariable("REDIS_URL");
            Console.WriteLine($"[leader] REDIS_URL present? {!string.IsNullOrWhiteSpace(rawUrl)} len={(rawUrl?.Length ?? 0)}");

            // Configure leader-lock behavior
            var ttl = TimeSpan.FromSeconds(30);
            var waitForRedis = TimeSpan.FromSeconds(12);                 // how long to wait for initial connect
            var required = Environment.GetEnvironmentVariable("LEADER_LOCK_REQUIRED") == "1";

            // Try to init leader lock (non-blocking if Redis is down unless required)
            var ok = await InitLeaderLockAsync(ttl, waitForRedis, required);
            if (!ok)
            {
                Console.WriteLine("[leader] Another instance holds the lock. Exiting.");
                return;
            }

            // Create services AFTER lock decision
            ReminderService = new ReminderService();
            Console.WriteLine("[reminders] service ready.");

            BotInstance = new Bot(ReminderService);

            // Clean shutdown
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; OnProcessExit(null, EventArgs.Empty); };

            await BotInstance.RunAsync();
        }

        // --- Redis lock helpers ---

        private static string? GetRedisUrlFromEnv()
        {
            foreach (var k in new[] { "REDIS_URL", "UPSTASH_REDIS_URL", "REDIS", "REDIS_CONNECTION_STRING" })
            {
                var v = Environment.GetEnvironmentVariable(k);
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        private static async Task<bool> InitLeaderLockAsync(TimeSpan ttl, TimeSpan waitBeforeGivingUp, bool required)
        {
            var url = GetRedisUrlFromEnv();
            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("[leader] No Redis URL found. Running WITHOUT leader lock.");
                return true; // or 'return !required;' if you want to enforce having Redis
            }

            var options = ConfigurationOptions.Parse(url, ignoreUnknown: true);
            options.AbortOnConnectFail = false; // keep retrying in background
            options.ConnectRetry = 5;
            options.ConnectTimeout = 15000;
            options.SyncTimeout = 15000;
            options.KeepAlive = 15;
            options.ResolveDns = true;

            try
            {
                _mux = await ConnectionMultiplexer.ConnectAsync(options);

                // Useful diagnostics
                _mux.ConnectionFailed += (_, e) => Console.WriteLine($"[redis] ConnectionFailed: {e.FailureType} {e.Exception?.Message}");
                _mux.ConnectionRestored += (_, __) => Console.WriteLine("[redis] ConnectionRestored");
                _mux.ConfigurationChanged += (_, __) => Console.WriteLine("[redis] ConfigurationChanged");

                // Wait briefly for initial connectivity, then proceed
                var sw = Stopwatch.StartNew();
                while (!_mux.IsConnected && sw.Elapsed < waitBeforeGivingUp)
                    await Task.Delay(500);

                if (!_mux.IsConnected)
                {
                    Console.WriteLine($"[leader] Redis not reachable after {waitBeforeGivingUp.TotalSeconds:n0}s.");
                    return !required; // proceed without lock if not required
                }

                _db = _mux.GetDatabase();
                _lockKey = Environment.GetEnvironmentVariable("LEADER_LOCK_KEY") ?? _lockKey;

                var instanceId = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID");
                if (!string.IsNullOrWhiteSpace(instanceId))
                    _lockValue = instanceId;

                // Acquire: NX + TTL
                var acquired = await _db.StringSetAsync(_lockKey, _lockValue, ttl, When.NotExists);
                var ep = _mux.GetEndPoints().FirstOrDefault();
                Console.WriteLine($"[leader] Lock '{_lockKey}' → {(acquired ? "acquired" : "already held")} | endpoint={ep}");

                if (!acquired) return false; // another instance is leader

                // Start renewer
                _renewCts = new CancellationTokenSource();
                _ = Task.Run(() => RenewLoopAsync(ttl, _renewCts.Token));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[leader] Redis connect error: " + ex.Message);
                // Continue without lock unless strictly required
                return !required;
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

                    // Renew TTL only if we still own the lock (transactional, no Lua)
                    var tran = _db.CreateTransaction();
                    tran.AddCondition(Condition.StringEqual(_lockKey, _lockValue));
                    _ = tran.KeyExpireAsync(_lockKey, ttl);

                    bool ok = await tran.ExecuteAsync();
                    if (!ok)
                    {
                        Console.WriteLine("[leader] Lost leader lock; exiting.");
                        Environment.Exit(0);
                    }
                }
                catch (TaskCanceledException) { /* normal on shutdown */ }
                catch (Exception ex)
                {
                    Console.WriteLine("[leader] Renew error: " + ex.Message);
                    // try again next tick
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
                    // Release lock only if we still own it
                    var tran = _db.CreateTransaction();
                    tran.AddCondition(Condition.StringEqual(_lockKey, _lockValue));
                    _ = tran.KeyDeleteAsync(_lockKey);
                    _ = tran.Execute(); // sync is fine during shutdown
                }

                _mux?.Dispose();
            }
            catch { /* best-effort cleanup */ }
        }
    }
}