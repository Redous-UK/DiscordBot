using System;


namespace MyDiscordBot.Models
{
    public class Reminder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ulong GuildId { get; set; } // NEW: where to deliver
        public ulong ChannelId { get; set; } // NEW: where to deliver
        public ulong UserId { get; set; } // helps mention and grouping
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset DueAtUtc { get; set; } // NEW: store absolute UTC time
        public int? RepeatMinutes { get; set; } // keep simple repeat semantics
    }
}