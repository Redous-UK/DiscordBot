using Discord;
using Discord.WebSocket;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RemindCommand : ILegacyCommand
    {
        public string Name => "remind";
        public string Description => "Sets a reminder. Usage: !remind <time> <message> (e.g., !remind 10m Take a break). Optional: --every <minutes> for repeats.";
        public string Category => "🔔 Reminders & Notifications";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            // Basic usage check
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync(
                    "Usage: `!remind <time> <message>` (e.g., `!remind 10m Take a break`)\n" +
                    "Optional repeat: `!remind 10m Take a break --every 60` (minutes)");
                return;
            }

            // Resolve the service at runtime (avoids constructor timing/null issues)
            var reminderService = Bot.BotInstance?.Services?.Reminders;
            if (reminderService == null)
            {
                await message.Channel.SendMessageAsync("❌ Reminder service is not available yet. Try again in a moment.");
                return;
            }

            // Parse optional repeat flag: --every <minutes>
            int? repeatMinutes = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--every", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mins) && mins > 0)
                    {
                        repeatMinutes = mins;
                        // remove the flag and value from args so the message join works cleanly
                        args = args.Where((_, idx) => idx != i && idx != i + 1).ToArray();
                    }
                    break; // only honor the first occurrence
                }
            }

            var timeToken = args[0];
            var text = string.Join(" ", args.Skip(1)).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                await message.Channel.SendMessageAsync("❌ Please provide a reminder message.");
                return;
            }

            if (!TryParseDelayTokenToUtc(timeToken, out DateTimeOffset dueAtUtc))
            {
                await message.Channel.SendMessageAsync("Invalid time format. Use formats like `10m`, `2h`, `1d`.");
                return;
            }

            // Delivery context
            ulong guildId = 0UL;
            if (message.Channel is SocketGuildChannel gc)
                guildId = gc.Guild.Id;
            ulong channelId = message.Channel.Id;

            var repeatEvery = repeatMinutes.HasValue ? TimeSpan.FromMinutes(repeatMinutes.Value) : (TimeSpan?)null;

            // Preferred: new overload with guild/channel + UTC + TimeSpan repeat
            // Ensure your ReminderService defines:
            // AddReminder(ulong guildId, ulong channelId, ulong userId, string text, DateTimeOffset dueAtUtc, TimeSpan? repeatEvery)
            reminderService.AddReminder(guildId, channelId, message.Author.Id, text, dueAtUtc, repeatEvery);

            var eta = dueAtUtc - DateTimeOffset.UtcNow;
            await message.Channel.SendMessageAsync(
                $"⏰ I’ll remind you in {DescribeDelta(eta)}: \"{text}\"" +
                (repeatMinutes.HasValue ? $" (repeats every {repeatMinutes}m)" : ""));
        }

        // Accepts Xm / Xh / Xd and returns an absolute UTC timestamp
        private static bool TryParseDelayTokenToUtc(string input, out DateTimeOffset dueAtUtc)
        {
            dueAtUtc = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(input) || input.Length < 2)
                return false;

            char unit = char.ToLowerInvariant(input[^1]);
            var numberPart = input[..^1];
            if (!int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
                return false;

            var now = DateTimeOffset.UtcNow;
            dueAtUtc = unit switch
            {
                'm' => now.AddMinutes(value),
                'h' => now.AddHours(value),
                'd' => now.AddDays(value),
                _ => DateTimeOffset.MinValue
            };
            return dueAtUtc != DateTimeOffset.MinValue;
        }

        private static string DescribeDelta(TimeSpan span)
        {
            if (span.TotalSeconds < 1) return "0s";
            if (span.TotalMinutes < 1) return $"{span.Seconds}s";
            if (span.TotalHours < 1) return $"{span.Minutes}m {span.Seconds}s";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
    }
}