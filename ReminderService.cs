using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyDiscordBot.Services
{
    public class ReminderService
    {
        private readonly DiscordSocketClient _client;
        private const string ReminderFile = "reminders.json";
        private static List<Reminder> _reminders = new();

        public ReminderService(DiscordSocketClient client)
        {
            _client = client;
            LoadReminders();
            _ = StartReminderLoopAsync();
        }

        private void LoadReminders()
        {
            if (File.Exists(ReminderFile))
            {
                _reminders = JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(ReminderFile));
            }
        }

        private void SaveReminders()
        {
            var json = JsonSerializer.Serialize(_reminders, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ReminderFile, json);
        }

        private async Task StartReminderLoopAsync()
        {
            while (true)
            {
                var now = DateTime.UtcNow;

                var dueReminders = _reminders.FindAll(r => r.RemindAt <= now);
                foreach (var reminder in dueReminders)
                {
                    if (_client.GetChannel(reminder.ChannelId) is IMessageChannel channel)
                    {
                        await channel.SendMessageAsync($"⏰ <@{reminder.UserId}>, reminder: {reminder.Message}");
                    }

                    if (!reminder.Repeat)
                        _reminders.Remove(reminder);
                    else
                        reminder.RemindAt = reminder.RemindAt.AddDays(1);
                }

                SaveReminders();
                await Task.Delay(10000);
            }
        }

        public void AddReminder(ulong userId, ulong channelId, DateTime remindAt, string message, bool repeat)
        {
            _reminders.Add(new Reminder
            {
                UserId = userId,
                ChannelId = channelId,
                RemindAt = remindAt,
                Message = message,
                Repeat = repeat
            });

            SaveReminders();
        }

        public List<string> GetReminders(ulong userId)
        {
            var list = new List<string>();

            foreach (var reminder in _reminders)
            {
                if (reminder.UserId == userId)
                {
                    var formatted = $"⏰ {reminder.RemindAt:G} - {reminder.Message} {(reminder.Repeat ? "(repeats)" : "")}";
                    list.Add(formatted);
                }
            }

            return list;
        }

        private class Reminder
        {
            public ulong UserId { get; set; }
            public ulong ChannelId { get; set; }
            public DateTime RemindAt { get; set; }
            public string Message { get; set; }
            public bool Repeat { get; set; }
        }
    }
}