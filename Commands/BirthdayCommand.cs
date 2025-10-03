using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class BirthdayCommand : ILegacyCommand
    {
        public string Name => "birthday";

        public string Description => ("**Birthday Usage:**\n" +
            "`!birthday set YYYY-MM-DD` – Set your birthday date\n" +
            "`!birthday get @user` – Show the birthday of the specified user\n" +
            "`!birthday today` – Checks if there is anyone's birthday today\n" +
            "`!birthday tomorrow` - Checks if there is anyone's birthday tomorrow\n" +
            "`!birthday month [name|#]` - Lists birthdays in the given month");

        public string Category => "⚙️ Settings & Config";

        // ---- Persistent storage path handling ----
        private const string DefaultFileName = "birthdays.json";

        private static string GetPersistentFilePath()
        {
            // If BIRTHDAY_STORE_PATH points to a file, use it directly.
            var explicitPath = Environment.GetEnvironmentVariable("BIRTHDAY_STORE_PATH");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (explicitPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(explicitPath)!);
                    return explicitPath;
                }
                // If it's a directory, combine with our filename.
                Directory.CreateDirectory(explicitPath);
                return Path.Combine(explicitPath, DefaultFileName);
            }

            // Else prefer DATA_DIR, then /var/data, else current dir as a last resort
            var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? "/var/data";
            Directory.CreateDirectory(dataDir);
            return Path.Combine(dataDir, DefaultFileName);
        }

        private static string BirthdaysPath => GetPersistentFilePath();

        // Try to migrate legacy file ("birthdays.json" in working/app dir) once.
        private static void MigrateLegacyIfNeeded()
        {
            try
            {
                var legacyCandidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory ?? "", DefaultFileName),
                    Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName)
                };

                if (!File.Exists(BirthdaysPath))
                {
                    foreach (var legacy in legacyCandidates)
                    {
                        if (File.Exists(legacy))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(BirthdaysPath)!);
                            File.Copy(legacy, BirthdaysPath, overwrite: false);
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Swallow; optional: log if you have a logger
            }
        }

        public class BirthdayEntry
        {
            public DateTime Date { get; set; }
            public string Username { get; set; } = string.Empty;
        }

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("This command must be used in a server.");
                return;
            }

            // Ensure we’ve migrated any legacy file once per process
            MigrateLegacyIfNeeded();

            var guild = guildChannel.Guild;
            var guildId = guild.Id;
            var userId = message.Author.Id;
            var data = LoadBirthdays();

            string subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

            if (subcommand == "set" && args.Length > 1)
            {
                // Accept strict ISO formats to avoid locale surprises
                var input = args[1].Trim();
                DateTime birthday;

                // Try exact "yyyy-MM-dd" first; then fallback to general parsing as a last resort
                if (!DateTime.TryParseExact(input, new[] { "yyyy-MM-dd", "yyyy/MM/dd" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out birthday) &&
                    !DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out birthday))
                {
                    await message.Channel.SendMessageAsync("❌ Please use a valid date format, e.g., `YYYY-MM-DD`.");
                    return;
                }

                data[$"{guildId}-{userId}"] = new BirthdayEntry
                {
                    Date = birthday,
                    Username = message.Author.Username
                };

                SaveBirthdays(data);
                await message.Channel.SendMessageAsync($"✅ Birthday set for {birthday:MMMM dd}");
            }
            else if (subcommand == "get")
            {
                // Optional: allow @mentions or fallback to the author
                var target = message.MentionedUsers.FirstOrDefault() ?? message.Author;
                var key = $"{guildId}-{target.Id}";
                if (data.TryGetValue(key, out var entry))
                {
                    await message.Channel.SendMessageAsync($"🎂 **{entry.Username}** → {entry.Date:MMMM dd}");
                }
                else
                {
                    await message.Channel.SendMessageAsync("📭 No birthday saved for that user.");
                }
            }
            else if (subcommand == "today")
            {
                var today = DateTime.Today;
                var matches = data
                    .Where(entry =>
                        entry.Key.StartsWith($"{guildId}-") &&
                        entry.Value.Date.Month == today.Month &&
                        entry.Value.Date.Day == today.Day)
                    .Select(entry => entry.Value.Username)
                    .ToList();

                if (matches.Count == 0)
                    await message.Channel.SendMessageAsync("🎈 Nobody has a birthday today.");
                else
                    await message.Channel.SendMessageAsync($"🎉 Today's birthdays: {string.Join(", ", matches)} 🎂");
            }
            else if (subcommand == "tomorrow")
            {
                var t = DateTime.Today.AddDays(1);
                var matches = data
                    .Where(entry =>
                        entry.Key.StartsWith($"{guildId}-") &&
                        entry.Value.Date.Month == t.Month &&
                        entry.Value.Date.Day == t.Day)
                    .Select(entry => entry.Value.Username)
                    .ToList();

                if (matches.Count == 0)
                    await message.Channel.SendMessageAsync("🎈 Nobody has a birthday tomorrow.");
                else
                    await message.Channel.SendMessageAsync($"🎉 Tomorrow's birthdays: {string.Join(", ", matches)} 🎂");
            }
            else if (subcommand == "list")
            {
                var birthdayList = data
                    .Where(entry => entry.Key.StartsWith($"{guildId}-"))
                    .Select(entry => $"{entry.Value.Username}: {entry.Value.Date:MMMM dd}")
                    .OrderBy(line => line)
                    .ToList();

                if (birthdayList.Count == 0)
                    await message.Channel.SendMessageAsync("📭 No birthdays saved for this server.");
                else
                    await message.Channel.SendMessageAsync($"📅 **Saved Birthdays:**\n{string.Join("\n", birthdayList)}");
            }
            else if (subcommand == "month")
            {
                int targetMonth;
                if (args.Length > 1)
                {
                    var token = args[1].Trim();
                    if (int.TryParse(token, out int m) && m >= 1 && m <= 12)
                    {
                        targetMonth = m;
                    }
                    else if (DateTime.TryParseExact(token, "MMMM", CultureInfo.InvariantCulture,
                             DateTimeStyles.AllowWhiteSpaces, out var parsed))
                    {
                        targetMonth = parsed.Month;
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync("❌ Invalid month. Use a full month name (e.g., `August`) or a number 1–12.");
                        return;
                    }
                }
                else
                {
                    targetMonth = DateTime.Today.Month;
                }

                var matches = data
                    .Where(entry => entry.Key.StartsWith($"{guildId}-") &&
                                    entry.Value.Date.Month == targetMonth)
                    .OrderBy(entry => entry.Value.Date.Day)
                    .Select(entry => $"{entry.Value.Username}: {entry.Value.Date:MMMM dd}")
                    .ToList();

                if (matches.Count == 0)
                    await message.Channel.SendMessageAsync($"📭 No birthdays found in {new DateTime(1, targetMonth, 1):MMMM}.");
                else
                    await message.Channel.SendMessageAsync($"📅 **Birthdays in {new DateTime(1, targetMonth, 1):MMMM}:**\n{string.Join("\n", matches)}");
            }
            else
            {
                await message.Channel.SendMessageAsync("**Usage:**\n" +
                    "`!birthday set YYYY-MM-DD` – Save your birthday\n" +
                    "`!birthday get @user` – Show the saved birthday for a user\n" +
                    "`!birthday today` – Show birthdays happening today\n" +
                    "`!birthday tomorrow` – Show birthdays happening tomorrow\n" +
                    "`!birthday month [name|#]` – Show birthdays for a month (e.g., `!birthday month August`)\n" +
                    "`!birthday list` – Show all saved birthdays in this server");
            }
        }

        // ---- Storage helpers ----

        private Dictionary<string, BirthdayEntry> LoadBirthdays()
        {
            try
            {
                if (!File.Exists(BirthdaysPath)) return new();
                var json = File.ReadAllText(BirthdaysPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<Dictionary<string, BirthdayEntry>>(json) ?? new();
            }
            catch
            {
                return new();
            }
        }

        private void SaveBirthdays(Dictionary<string, BirthdayEntry> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BirthdaysPath)!);

            // Atomic write: write to temp file then replace/move
            var tmp = BirthdaysPath + ".tmp";
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(tmp, json, Encoding.UTF8);
            if (File.Exists(BirthdaysPath))
            {
                try { File.Replace(tmp, BirthdaysPath, null); }
                catch { File.Delete(BirthdaysPath); File.Move(tmp, BirthdaysPath); }
            }
            else
            {
                File.Move(tmp, BirthdaysPath);
            }
        }
    }
}