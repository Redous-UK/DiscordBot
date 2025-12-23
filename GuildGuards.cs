using System;

public static class GuildGuards
{
    public static ulong AgencyGuildId { get; } =
        ulong.TryParse(Environment.GetEnvironmentVariable("AGENCY_GUILD_ID"), out var id) ? id : 0;

    public static bool IsAgencyGuild(ulong guildId)
        => AgencyGuildId != 0 && guildId == AgencyGuildId;
}
