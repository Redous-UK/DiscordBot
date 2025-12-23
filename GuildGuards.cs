using System;
using System.Collections.Generic;
using System.Linq;

internal static class GuildGuards
{
    private static readonly HashSet<ulong> _agencyGuildIds = LoadAgencyGuildIds();

    public static bool IsAgencyGuild(ulong guildId)
        => _agencyGuildIds.Count > 0 && _agencyGuildIds.Contains(guildId);

    private static HashSet<ulong> LoadAgencyGuildIds()
    {
        // Prefer plural; fall back to old singular for backward compatibility
        var raw = Environment.GetEnvironmentVariable("AGENCY_GUILD_IDS");
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("AGENCY_GUILD_ID");

        var set = new HashSet<ulong>();
        if (string.IsNullOrWhiteSpace(raw))
            return set;

        foreach (var token in raw.Split(new[] { ',', ' ', ';', '\t', '\n', '\r' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ulong.TryParse(token, out var id) && id != 0)
                set.Add(id);
        }

        return set;
    }
}