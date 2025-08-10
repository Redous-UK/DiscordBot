using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace MyDiscordBot
{
    public static class Program
    {
        public static Bot BotInstance { get; private set; }

        public static async Task Main(string[] args)
        {
            // Try to acquire leadership
            if (!await TryAcquireLeaderAsync(TimeSpan.FromSeconds(15)))
            {
                Console.WriteLine("[leader] Another instance is active. Exiting.");
                return; // <-- do not start the bot
            }

            BotInstance = new Bot();
            await BotInstance.RunAsync();
        }

        private static async Task<bool> TryAcquireLeaderAsync(TimeSpan ttl)
        {
            var url = Environment.GetEnvironmentVariable("REDIS_URL");
            Console.WriteLine($"[leader] REDIS_URL present? {!string.IsNullOrWhiteSpace(url)} len={url?.Length ?? 0}");

            var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");
            if (string.IsNullOrWhiteSpace(redisUrl))
            {
                Console.WriteLine("[leader] REDIS_URL not set; running without lock (not safe).");
                return true; // fallback if you really have no Redis
            }

            var mux = await ConnectionMultiplexer.ConnectAsync(redisUrl);
            var db = mux.GetDatabase();

            // Key per “bot”; adjust if you run multiple bots
            var key = "mydiscordbot:leader";
            var val = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID")
                      ?? Guid.NewGuid().ToString("n");

            // SET key value NX EX <ttl>  → only first wins
            var acquired = await db.StringSetAsync(key, val, ttl, When.NotExists);
            if (!acquired) return false;

            // Keep the lock alive
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(ttl - TimeSpan.FromSeconds(3));
                    // extend only if still leader
                    var current = await db.StringGetAsync(key);
                    if (current != val) break;
                    await db.StringSetAsync(key, val, ttl, When.Always);
                }
            });

            return true;
        }
    }
}