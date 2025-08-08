using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RemindCommand : ILegacyCommand
    {
        public string Name => "remind";
        public string Description => "Sets a reminder. Usage: !remind <time> <message> (e.g., !remind 10m Take a break)";
        public string Category => "🔔 Reminders & Notifications";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            // Basic usage check
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: `!remind <time> <message>` (e.g., `!remind 10m Take a break`)");
                return;
            }

            // Resolve the service at runtime (avoids constructor timing/null issues)
            var reminderService = Program.BotInstance?.ReminderService;
            if (reminderService == null)
            {
                await message.Channel.SendMessageAsync("❌ Reminder service is not available yet. Try again in a moment.");
                return;
            }

            var timeInput = args[0];
            var reminderMessage = string.Join(" ", args.Skip(1));

            if (!TryParseTime(timeInput, out DateTime remindTimeUtc))
            {
                await message.Channel.SendMessageAsync("Invalid time format. Use formats like `10m`, `2h`, `1d`.");
                return;
            }

            var entry = new ReminderEntry
            {
                Message = reminderMessage,
                Time = remindTimeUtc
            };

            reminderService.AddReminder(message.Author.Id, entry);

            await message.Channel.SendMessageAsync($":alarm_clock: I'll remind you in {DescribeDelta(remindTimeUtc - DateTime.UtcNow)}: \"{reminderMessage}\"");
        }

        // Accepts Xm / Xh / Xd (minutes/hours/days). Stores UTC trigger time.
        private bool TryParseTime(string input, out DateTime resultUtc)
        {
            resultUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(input) || input.Length < 2)
                return false;

            char unit = char.ToLowerInvariant(input[^1]);
            string numberPart = input[..^1];

            if (!int.TryParse(numberPart, out int value) || value <= 0)
                return false;

            try
            {
                resultUtc = unit switch
                {
                    'm' => DateTime.UtcNow.AddMinutes(value),
                    'h' => DateTime.UtcNow.AddHours(value),
                    'd' => DateTime.UtcNow.AddDays(value),
                    _ => DateTime.MinValue
                };

                return resultUtc != DateTime.MinValue;
            }
            catch
            {
                return false;
            }
        }

        private string DescribeDelta(TimeSpan span)
        {
            if (span.TotalSeconds < 0) return "0s";

            if (span.TotalMinutes < 1)
                return $"{span.Seconds}s";
            if (span.TotalHours < 1)
                return $"{span.Minutes}m {span.Seconds}s";
            if (span.TotalDays < 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
    }
}