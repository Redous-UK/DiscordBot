using System;

namespace MyDiscordBot.Models
{
    public class Reminder
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong UserId { get; set; }
        public string Message { get; set; }
        public DateTime TriggerTime { get; set; }
        public TimeSpan? RepeatInterval { get; set; } // Null if one-time
    }
}