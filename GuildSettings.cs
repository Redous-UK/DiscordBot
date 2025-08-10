using System.Collections.Generic;

namespace MyDiscordBot
{
    public class GuildSettings
    {
        public ulong BirthdayChannelId { get; set; }
        public List<string> LogCategories { get; set; } = new();
        public bool DebugEnabled { get; set; }
        public string Nickname { get; set; } = null!;
    }
}
