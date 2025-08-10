using System;

namespace MyDiscordBot.Models
{
    public class Reminder
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>When the reminder should fire (UTC).</summary>
        public DateTime Time { get; set; }

        /// <summary>The reminder text.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Optional repeat interval in minutes. Null/0 = not recurring.</summary>
        public int? RepeatMinutes { get; set; }
    }
}