using Discord;
using Discord.WebSocket;
using MyDiscordBot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MyDiscordBot.Services
{
    using MyDiscordBot.Models;

    /// <summary>
    /// Simple JSON-backed store: guildId -> GuildSettings
    /// Persists to DATA_DIR (or /var/data) as guildsettings.json
    /// </summary>
    public class GuildSettingsService
    {
        private readonly object _sync = new();
        private readonly string _path;
        private Dictionary<ulong, GuildSettings> _store = new();

        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GuildSettingsService(string? explicitPath = null)
        {
            var dir = ResolveDataDir();
            _path = explicitPath ?? Path.Combine(dir, "guildsettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            Load();
        }

        public ulong? GetLeaveChannelId(ulong guildId)
        {
            lock (_sync)
            {
                return _store.TryGetValue(guildId, out var gs) ? gs.LeaveAnnounceChannelId : null;
            }
        }

        public void SetLeaveChannelId(ulong guildId, ulong? channelId)
        {
            lock (_sync)
            {
                if (!_store.TryGetValue(guildId, out var gs))
                    _store[guildId] = gs = new GuildSettings();
                gs.LeaveAnnounceChannelId = channelId;
                Save_NoLock();
            }
        }

        public ulong? GetJoinChannelId(ulong guildId)
        {
            lock (_sync)
            {
                return _store.TryGetValue(guildId, out var gs) ? gs.JoinAnnounceChannelId : null;
            }
        }

        public void SetJoinChannelId(ulong guildId, ulong? channelId)
        {
            lock (_sync)
            {
                if (!_store.TryGetValue(guildId, out var gs))
                    _store[guildId] = gs = new GuildSettings();

                gs.JoinAnnounceChannelId = channelId;
                Save_NoLock();
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) { _store = new(); return; }
                var json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<Dictionary<ulong, GuildSettings>>(json, _json);
                _store = data ?? new();
            }
            catch
            {
                _store = new();
            }
        }

        private void Save_NoLock()
        {
            var json = JsonSerializer.Serialize(_store, _json);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path))
            {
                try { File.Replace(tmp, _path, null); }
                catch { File.Delete(_path); File.Move(tmp, _path); }
            }
            else File.Move(tmp, _path);
        }

        private static string ResolveDataDir()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("DATA_DIR"),
                Environment.GetEnvironmentVariable("RENDER_DISK_PATH"),
                "/var/data",
                "/data"
            };
            foreach (var p in candidates)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    try { Directory.CreateDirectory(p); return p; } catch { }
                }
            }
            return AppContext.BaseDirectory;
        }
    }
}

namespace MyDiscordBot.Commands
{
    using MyDiscordBot.Services;

    /// <summary>
    /// Admin command to configure the leave announcement channel per guild.
    /// Usage:
    ///   !leaves set #channel   → post leave notices to that channel
    ///   !leaves set here       → use the current channel
    ///   !leaves off            → disable leave announcements
    ///   !leaves show           → display current setting
    ///   !leaves test           → send a test message to the configured channel
    /// </summary>
    public class LeaveAnnounceCommand : ILegacyCommand
    {
        public string Name => "leaves";
        public string Description => "Configure the channel for leave announcements (admin).";
        public string Category => "⚙️ Settings & Config";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel gch)
            {
                await message.Channel.SendMessageAsync("Run this in a server.");
                return;
            }

            var member = gch.GetUser(message.Author.Id);
            if (member == null || !member.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("🚫 Admins only.");
                return;
            }

            var settings = Program.BotInstance?.Services?.GuildSettings;
            if (settings == null)
            {
                await message.Channel.SendMessageAsync("⚠️ Guild settings service not available.");
                return;
            }

            var sub = args.FirstOrDefault()?.ToLowerInvariant();
            switch (sub)
            {
                case "set":
                    {
                        if (args.Length < 2)
                        {
                            await message.Channel.SendMessageAsync("Usage: `!leaves set #channel` or `!leaves set here`");
                            return;
                        }
                        var token = args[1];
                        SocketTextChannel? target = null;

                        // If a channel was mentioned, prefer that
                        target = message.MentionedChannels.FirstOrDefault() as SocketTextChannel;

                        if (target == null)
                        {
                            if (string.Equals(token, "here", StringComparison.OrdinalIgnoreCase))
                                target = gch as SocketTextChannel;
                            else if (TryParseChannelId(token, out var id))
                                target = gch.Guild.GetTextChannel(id);
                        }

                        if (target == null)
                        {
                            await message.Channel.SendMessageAsync("❌ Couldn’t resolve that channel. Mention it or use `here`.");
                            return;
                        }

                        settings.SetLeaveChannelId(gch.Guild.Id, target.Id);
                        await message.Channel.SendMessageAsync($"✅ Leave announcements will go to <#{target.Id}>.");
                        return;
                    }
                case "off":
                    {
                        settings.SetLeaveChannelId(gch.Guild.Id, null);
                        await message.Channel.SendMessageAsync("✅ Leave announcements disabled.");
                        return;
                    }
                case "show":
                    {
                        var id = settings.GetLeaveChannelId(gch.Guild.Id);
                        await message.Channel.SendMessageAsync(id.HasValue ? $"📣 Current channel: <#{id.Value}>" : "📣 Currently disabled.");
                        return;
                    }
                case "test":
                    {
                        var id = settings.GetLeaveChannelId(gch.Guild.Id);
                        if (!id.HasValue)
                        {
                            await message.Channel.SendMessageAsync("ℹ️ Not configured. Use `!leaves set #channel` first.");
                            return;
                        }
                        var ch = gch.Guild.GetTextChannel(id.Value);
                        if (ch == null)
                        {
                            await message.Channel.SendMessageAsync("⚠️ Configured channel no longer exists or I lack access.");
                            return;
                        }
                        await ch.SendMessageAsync($"🧪 Test: leave announcements will post here (requested by <@{message.Author.Id}>).");
                        await message.Channel.SendMessageAsync("✅ Test sent.");
                        return;
                    }
                default:
                    {
                        await message.Channel.SendMessageAsync(
                            "**Usage**\n" +
                            "`!leaves set #channel` – set the leave announcement channel\n" +
                            "`!leaves set here` – use the current channel\n" +
                            "`!leaves off` – disable announcements\n" +
                            "`!leaves show` – show current setting\n" +
                            "`!leaves test` – send a test message");
                        return;
                    }
            }
        }



        private static bool TryParseChannelId(string token, out ulong id)
        {
            id = 0UL;
            if (string.IsNullOrWhiteSpace(token)) return false;

            // Accept raw ID or <#1234567890>
            token = token.Trim();
            if (token.StartsWith("<#") && token.EndsWith(">"))
                token = token.Substring(2, token.Length - 3);

            return ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }
    }
}

public class JoinAnnounceCommand : ILegacyCommand
{
    public string Name => "joins";
    public string Description => "Configure the channel for join announcements (admin).";
    public string Category => "⚙️ Settings & Config";
    public string Usage => "!joins set #channel | !joins set here | !joins off | !joins show | !joins test";

    public async Task ExecuteAsync(SocketMessage message, string[] args)
    {
        if (message.Channel is not SocketGuildChannel gch)
        {
            await message.Channel.SendMessageAsync("Run this in a server.");
            return;
        }

        var member = gch.GetUser(message.Author.Id);
        if (member == null || !member.GuildPermissions.Administrator)
        {
            await message.Channel.SendMessageAsync("🚫 Admins only.");
            return;
        }

        // Use your services container (same as leaves)
        var settings = Bot.BotInstance?.Services?.GuildSettings;
        if (settings == null)
        {
            await message.Channel.SendMessageAsync("⚠️ Guild settings service not available.");
            return;
        }

        var sub = args.FirstOrDefault()?.ToLowerInvariant();
        switch (sub)
        {
            case "set":
                {
                    if (args.Length < 2)
                    {
                        await message.Channel.SendMessageAsync("Usage: `!joins set #channel` or `!joins set here`");
                        return;
                    }

                    var token = args[1];
                    SocketTextChannel? target = message.MentionedChannels.FirstOrDefault() as SocketTextChannel;

                    if (target == null)
                    {
                        if (string.Equals(token, "here", StringComparison.OrdinalIgnoreCase))
                            target = gch as SocketTextChannel;
                        else if (TryParseChannelId(token, out var id))
                            target = gch.Guild.GetTextChannel(id);
                    }

                    if (target == null)
                    {
                        await message.Channel.SendMessageAsync("❌ Couldn’t resolve that channel. Mention it or use `here`.");
                        return;
                    }

                    settings.SetJoinChannelId(gch.Guild.Id, target.Id);
                    await message.Channel.SendMessageAsync($"✅ Join announcements will go to <#{target.Id}>.");
                    return;
                }
            case "off":
                {
                    settings.SetJoinChannelId(gch.Guild.Id, null);
                    await message.Channel.SendMessageAsync("✅ Join announcements disabled.");
                    return;
                }
            case "show":
                {
                    var id = settings.GetJoinChannelId(gch.Guild.Id);
                    await message.Channel.SendMessageAsync(id.HasValue ? $"🎉 Current channel: <#{id.Value}>" : "🎉 Currently disabled.");
                    return;
                }
            case "test":
                {
                    var id = settings.GetJoinChannelId(gch.Guild.Id);
                    if (!id.HasValue)
                    {
                        await message.Channel.SendMessageAsync("ℹ️ Not configured. Use `!joins set #channel` first.");
                        return;
                    }

                    var ch = gch.Guild.GetTextChannel(id.Value);
                    if (ch == null)
                    {
                        await message.Channel.SendMessageAsync("⚠️ Configured channel no longer exists or I lack access.");
                        return;
                    }

                    await ch.SendMessageAsync($"🧪 Test: join announcements will post here (requested by <@{message.Author.Id}>).");
                    await message.Channel.SendMessageAsync("✅ Test sent.");
                    return;
                }
            default:
                {
                    await message.Channel.SendMessageAsync(
                        "**Usage**\n" +
                        "`!joins set #channel` – set the join announcement channel\n" +
                        "`!joins set here` – use the current channel\n" +
                        "`!joins off` – disable announcements\n" +
                        "`!joins show` – show current setting\n" +
                        "`!joins test` – send a test message");
                    return;
                }
        }
    }

    private static bool TryParseChannelId(string token, out ulong id)
    {
        id = 0UL;
        if (string.IsNullOrWhiteSpace(token)) return false;

        token = token.Trim();
        if (token.StartsWith("<#") && token.EndsWith(">"))
            token = token.Substring(2, token.Length - 3);

        return ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }
}

namespace MyDiscordBot.Background
{
    using MyDiscordBot.Services;

    /// <summary>
    /// Wires the Discord client to announce member leaves in the configured channel.
    /// </summary>
    public static class MemberEvents
    {
        private static bool _wired;
        public static void Wire(DiscordSocketClient client, GuildSettingsService settings, Action<string>? log = null)
        {
            if (_wired) return; // avoid double subscription
            _wired = true;

            client.UserLeft += async (SocketGuild guild, SocketUser user) =>
            {
                try
                {
                    var channelId = settings.GetLeaveChannelId(guild.Id);
                    if (!channelId.HasValue) return;

                    var ch = guild.GetTextChannel(channelId.Value);
                    if (ch == null)
                    {
                        log?.Invoke($"Leave announce channel missing for guild {guild.Id}");
                        return;
                    }

                    var username = user.DiscriminatorValue != 0
                        ? $"{user.Username}#{user.Discriminator}"
                        : user.Username;

                    await ch.SendMessageAsync($"👋 **{username}** left the server.");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"UserLeft handler error: {ex.Message}");
                }
            };

            client.UserJoined += async (SocketGuildUser user) =>
            {
                try
                {
                    var guild = user.Guild;
                    var channelId = settings.GetJoinChannelId(guild.Id);
                    if (!channelId.HasValue) return;

                    var ch = guild.GetTextChannel(channelId.Value);
                    if (ch == null)
                    {
                        log?.Invoke($"Join announce channel missing for guild {guild.Id}");
                        return;
                    }

                    var username = $"{user.Username}#{user.Discriminator}";
                    await ch.SendMessageAsync($"🎉 **{username}** joined the server. Welcome!");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"UserJoined handler error: {ex.Message}");
                }
            };

        }
    }
}