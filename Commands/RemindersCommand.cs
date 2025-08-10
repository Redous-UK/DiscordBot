using Discord.WebSocket;
using System.Text;
using MyDiscordBot.Models;

namespace MyDiscordBot.Commands
{
    public class RemindersCommand : ILegacyCommand
    {
        public string Name => "reminders";
        public string Description => "List your reminders.";
        public string Category => "🔔 Reminders & Notifications";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

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

            // ✅ make element types explicit so deconstruction is unambiguous
            foreach ((Reminder entry, int idx) in list
                .OrderBy(r => r.Time)
                .Select((r, i) => (r, i + 1)))
            {
                var delta = entry.Time - DateTime.UtcNow;
                var when = delta.TotalSeconds <= 0 ? "due now" : $"in {DescribeDelta(delta)}";
                var recur = entry.RepeatMinutes is int m and > 0 ? $" • repeats every {DescribeDelta(TimeSpan.FromMinutes(m))}" : string.Empty;

                // include ID to allow deletion by id if you add a !reminders remove <id> later
                sb.AppendLine($"`#{idx}` — **{entry.Message}** • {when}{recur} • `ID:{entry.Id}`");
            }

            await message.Channel.SendMessageAsync(sb.ToString());
        }

        // Simple "2h 5m 10s" formatter (tweak to your taste)
        private static string DescribeDelta(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = -ts;

            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {(ts.Hours)}h";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}