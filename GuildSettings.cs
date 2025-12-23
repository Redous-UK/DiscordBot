using System;
using System.Collections.Generic;

namespace MyDiscordBot.Models
{
    public class GuildSettings
    {
        public bool DebugEnabled { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public ulong BirthdayChannelId { get; set; }
        public HashSet<string> LogCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public ulong? LeaveAnnounceChannelId { get; set; }
        public ulong? JoinAnnounceChannelId { get; set; }
        
    }
}