using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace MyDiscordBot.Commands
{
    public class WhereAmICommand : ILegacyCommand
    {
        public string Name => "whereami";
        public string Description => "Show which deployment instance is running this bot.";
        public string Category => "🔧 Utility";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            // Render/Env info
            string service = Environment.GetEnvironmentVariable("RENDER_SERVICE_NAME") ?? "n/a";
            string instance = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID") ?? Environment.MachineName;
            string commit = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") ?? "n/a";
            string tag = Environment.GetEnvironmentVariable("INSTANCE_TAG") ?? "n/a";
            string region = Environment.GetEnvironmentVariable("RENDER_REGION") ?? "n/a";

            // Uptime
            DateTime startedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            TimeSpan uptime = DateTime.UtcNow - startedUtc;

            // Redis (mask secrets; show host:port only)
            string redisHostPort = "not configured";
            try
            {
                var raw = Environment.GetEnvironmentVariable("REDIS_URL");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var u = new Uri(raw);
                    redisHostPort = $"{u.Host}:{u.Port}";
                }
            }
            catch { /* ignore parse errors */ }

            // .NET info (handy during upgrades)
            string dotnet = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

            string msg =
$@"**Where am I?**
• Service: `{service}`
• Instance: `{instance}`
• Commit: `{commit}`
• Tag: `{tag}`
• Region: `{region}`
• Uptime: `{FormatUptime(uptime)}`
• .NET: `{dotnet}`
• Redis: `{redisHostPort}`";

            await message.Channel.SendMessageAsync(msg);
        }

        private static string FormatUptime(TimeSpan ts)
        {
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}