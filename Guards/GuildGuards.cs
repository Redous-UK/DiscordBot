using System;
using System.Collections.Generic;

namespace MyDiscordBot.Guards
{
    public static class GuildGuards
    {
        private static HashSet<ulong> _agencyGuildIds = LoadAgencyGuildIds();

        public static IReadOnlyCollection<ulong> AgencyGuildIds => _agencyGuildIds;

        public static bool IsAgencyGuild(ulong guildId)
            => _agencyGuildIds.Count > 0 && _agencyGuildIds.Contains(guildId);

        public static void Reload()
        {
            _agencyGuildIds = LoadAgencyGuildIds();
        }

        private static HashSet<ulong> LoadAgencyGuildIds()
        {
            var raw =
                Environment.GetEnvironmentVariable("AGENCY_GUILD_IDS") ??
                Environment.GetEnvironmentVariable("AGENCY_GUILD_ID");

            var set = new HashSet<ulong>();

            if (string.IsNullOrWhiteSpace(raw))
                return set;

            foreach (var token in raw.Split(
                         new[] { ',', ' ', ';', '\t', '\n', '\r' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ulong.TryParse(token, out var id) && id != 0)
                    set.Add(id);
            }

            return set;
        }
    }
}