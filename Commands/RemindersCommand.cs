using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RemindersCommand : ILegacyCommand
    {
        public string Name => "reminders";
        public string Description => "Lists all your reminders.";
        public string Category => "🔧 Utility";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            // Optional: only allow in guilds (keep if you want parity with your earlier version)
            if (message.Channel is not SocketGuildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            // Resolve the service at runtime to avoid constructor/initialization timing issues
            var reminderService = Program.BotInstance?.ReminderService;
            if (reminderService == null)
            {
                await message.Channel.SendMessageAsync("❌ Reminder service is not available yet. Try again shortly.");
                return;
            }

            var list = reminderService.GetReminders(message.Author.Id);

            if (list == null || list.Count == 0)
            {
                await message.Channel.SendMessageAsync("📭 You have no reminders.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"📋 **Reminders for {message.Author.Username}:**");

            // Sort soonest first
            foreach (var (entry, idx) in list.OrderBy(r => r.Time).Select((r, i) => (r, i + 1)))
            {
                var delta = entry.Time - DateTime.UtcNow;
                var when = delta.TotalSeconds <= 0 ? "due now" : $"in {DescribeDelta(delta)}";
                // You can add absolute time if you want: entry.Time.ToString("u")
                sb.AppendLine($"`#{idx}` — **{entry.Message}** • {when}");
            }

            await message.Channel.SendMessageAsync(sb.ToString());
        }

        private string DescribeDelta(TimeSpan span)
        {
            if (span.TotalSeconds < 0) return "0s";
            if (span.TotalMinutes < 1) return $"{span.Seconds}s";
            if (span.TotalHours < 1) return $"{span.Minutes}m {span.Seconds}s";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
    }
}