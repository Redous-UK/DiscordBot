using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class BirthdayCommand : ILegacyCommand
    {
        public string Name => "birthday";
        //public string Description => "Commands to setup birthdays !birthday set @user /n !birthday get @user /n !birthday today /n !birthday tomorrow /n !birthday month ? !";

        public String Description => ("**Birthday Usage:**\n" +
                    "`!birthday set YYYY-MM_DD` – Set your birthday date\n" +
                    "`!birthday get @user` – Show the birthday of the specified user\n" +
                    "`!birthday today` – Checks if there is anyones birthday today\n" +
                    "`!birthday tomorrow` - Checks if there is anyones birthday tomorrow\n" +
                    "`!birthday month ?` - Checks and lists out peoples birthday for the given month");
        public string Category => "⚙️ Settings & Config";


        private const string FilePath = "birthdays.json";

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

            var guild = guildChannel.Guild;
            var guildId = guild.Id;
            var userId = message.Author.Id;
            var data = LoadBirthdays();

            string subcommand = args.Length > 0 ? args[0].ToLower() : "help";

            if (subcommand == "set" && args.Length > 1)
            {
                if (!DateTime.TryParse(args[1], out DateTime birthday))
                {
                    await message.Channel.SendMessageAsync("❌ Please use a valid date format (e.g., `YYYY-MM-DD`).");
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
                {
                    await message.Channel.SendMessageAsync("🎈 Nobody has a birthday today.");
                }
                else
                {
                    var list = string.Join(", ", matches);
                    await message.Channel.SendMessageAsync($"🎉 Today's birthdays: {list} 🎂");
                }
            }
            else if (subcommand == "tomorrow")
            {
                var tomorrow = DateTime.Today.AddDays(1);
                var matches = data
                    .Where(entry =>
                        entry.Key.StartsWith($"{guildId}-") &&
                        entry.Value.Date.Month == tomorrow.Month &&
                        entry.Value.Date.Day == tomorrow.Day)
                    .Select(entry => entry.Value.Username)
                    .ToList();

                if (matches.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎈 Nobody has a birthday tomorrow.");
                }
                else
                {
                    var list = string.Join(", ", matches);
                    await message.Channel.SendMessageAsync($"🎉 Tomorrow's birthdays: {list} 🎂");
                }
            }
            else if (subcommand == "list")
            {
                var birthdayList = data
                    .Where(entry => entry.Key.StartsWith($"{guildId}-"))
                    .Select(entry => $"{entry.Value.Username}: {entry.Value.Date:MMMM dd}")
                    .OrderBy(line => line)
                    .ToList();

                if (birthdayList.Count == 0)
                {
                    await message.Channel.SendMessageAsync("📭 No birthdays saved for this server.");
                }
                else
                {
                    var list = string.Join("\n", birthdayList);
                    await message.Channel.SendMessageAsync($"📅 **Saved Birthdays:**\n{list}");
                }
            }
            else if (subcommand == "month")
            {
                int targetMonth;

                if (args.Length > 1)
                {
                    // Try parse as number first (e.g., "8")
                    if (int.TryParse(args[1], out int monthNum) && monthNum >= 1 && monthNum <= 12)
                    {
                        targetMonth = monthNum;
                    }
                    else
                    {
                        // Try parse as month name (e.g., "july")
                        try
                        {
                            var parsedDate = DateTime.ParseExact(args[1], "MMMM", System.Globalization.CultureInfo.InvariantCulture);
                            targetMonth = parsedDate.Month;
                        }
                        catch
                        {
                            await message.Channel.SendMessageAsync("❌ Invalid month. Use a full month name or number 1-12.");
                            return;
                        }
                    }
                }
                else
                {
                    // Default: current month
                    targetMonth = DateTime.Today.Month;
                }

                var matches = data
                    .Where(entry =>
                        entry.Key.StartsWith($"{guildId}-") &&
                        entry.Value.Date.Month == targetMonth)
                    .OrderBy(entry => entry.Value.Date.Day)
                    .Select(entry => $"{entry.Value.Username}: {entry.Value.Date:MMMM dd}")
                    .ToList();

                if (matches.Count == 0)
                {
                    await message.Channel.SendMessageAsync($"📭 No birthdays found in {new DateTime(1, targetMonth, 1):MMMM}.");
                }
                else
                {
                    var list = string.Join("\n", matches);
                    await message.Channel.SendMessageAsync($"📅 **Birthdays in {new DateTime(1, targetMonth, 1):MMMM}:**\n{list}");
                }
            }


            else
            {
                await message.Channel.SendMessageAsync("**Usage:**\n" +
                    "`!birthday set YYYY-MM-DD` – Save your birthday\n" +
                    "`!birthday today` – Show birthdays happening today\n" +
                    "`!birthday tomorrow` – Show birthdays happening tomorrow\n" +
                    "`!birthday month [name|#]` – Show birthdays for the month (e.g. `!birthday month August`)\n" +
                    "`!birthday list` – Show all saved birthdays in this server");
            }
        }

        private Dictionary<string, BirthdayEntry> LoadBirthdays()
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, BirthdayEntry>>(File.ReadAllText(FilePath)) ?? new();
        }

        private void SaveBirthdays(Dictionary<string, BirthdayEntry> data)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}