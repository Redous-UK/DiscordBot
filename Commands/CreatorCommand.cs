using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    internal static class GuildGuards
    {
        public static ulong AgencyGuildId { get; } =
            ulong.TryParse(Environment.GetEnvironmentVariable("AGENCY_GUILD_ID"), out var id) ? id : 0;

        public static bool IsAgencyGuild(ulong guildId)
            => AgencyGuildId != 0 && guildId == AgencyGuildId;
    }

    internal sealed class CreatorProfile
    {
        public string Handle { get; set; } = "";       // @handle
        public string DisplayName { get; set; } = "";  // nice name
        public string TiktokUid { get; set; } = "";    // optional
        public string Region { get; set; } = "";       // optional
        public string Notes { get; set; } = "";        // optional

        public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public ulong AddedByDiscordUserId { get; set; }
    }

    internal sealed class CreatorStore
    {
        private readonly string _path;
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public CreatorStore(string path)
        {
            _path = path;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_path))
                File.WriteAllText(_path, "[]");
        }

        public List<CreatorProfile> LoadAll()
        {
            lock (_lock)
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<CreatorProfile>>(json, JsonOpts) ?? new List<CreatorProfile>();
            }
        }

        public void SaveAll(List<CreatorProfile> creators)
        {
            lock (_lock)
            {
                var tmp = _path + ".tmp";
                var bak = _path + ".bak";

                File.WriteAllText(tmp, JsonSerializer.Serialize(creators, JsonOpts));
                if (File.Exists(_path))
                    File.Copy(_path, bak, overwrite: true);

                File.Copy(tmp, _path, overwrite: true);
                File.Delete(tmp);
            }
        }

        public CreatorProfile? FindByHandle(string handle)
        {
            var norm = NormalizeHandle(handle);
            return LoadAll().FirstOrDefault(c => NormalizeHandle(c.Handle) == norm);
        }

        public static string NormalizeHandle(string handle)
        {
            handle = (handle ?? "").Trim();
            if (handle.Length == 0) return "@";
            if (!handle.StartsWith("@")) handle = "@" + handle;
            return handle.ToLowerInvariant();
        }
    }

    internal static class BotStores
    {
        public static readonly CreatorStore Creators =
            new CreatorStore(Environment.GetEnvironmentVariable("CREATOR_STORE_PATH") ?? "data/creators.json");
    }

    public class CreatorCommand : ILegacyCommand
    {
        public string Name => "creator";
        public string Description => "Agency creator commands (info/list/add/remove).";
        public string Category => "⚙️ Settings & Config";
        public string Usage => "!creator info <@handle> | !creator list | !creator add <@handle> <displayName> [uid=] [region=] [notes=] | !creator remove <@handle>";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            // Hard-lock to only the agency server
            if (message.Channel is not SocketGuildChannel gch)
            {
                await message.Channel.SendMessageAsync("❌ This command can only be used in the agency server.");
                return;
            }

            if (!GuildGuards.IsAgencyGuild(gch.Guild.Id))
            {
                await message.Channel.SendMessageAsync("❌ Agency commands are only available in the agency server.");
                return;
            }

            if (args.Length == 0)
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`");
                return;
            }

            var sub = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            switch (sub)
            {
                case "info":
                    await Info(message, rest);
                    break;
                case "list":
                    await List(message);
                    break;
                case "add":
                    await Add(message, gch, rest);
                    break;
                case "remove":
                case "delete":
                    await Remove(message, gch, rest);
                    break;
                default:
                    await message.Channel.SendMessageAsync($"Unknown subcommand `{sub}`.\nUsage: `{Usage}`");
                    break;
            }
        }

        private static bool IsAdmin(SocketGuildChannel gch, SocketUser author)
        {
            var member = gch.GetUser(author.Id);
            return member != null && member.GuildPermissions.Administrator;
        }

        private static async Task Info(SocketMessage message, string[] args)
        {
            if (args.Length < 1)
            {
                await message.Channel.SendMessageAsync("Usage: `!creator info <@handle>`");
                return;
            }

            var handle = args[0];
            var c = BotStores.Creators.FindByHandle(handle);

            if (c == null)
            {
                await message.Channel.SendMessageAsync($"Couldn’t find {CreatorStore.NormalizeHandle(handle)}.");
                return;
            }

            await message.Channel.SendMessageAsync(
                $"**{c.DisplayName}** ({c.Handle})\n" +
                $"UID: {(string.IsNullOrWhiteSpace(c.TiktokUid) ? "—" : c.TiktokUid)}\n" +
                $"Region: {(string.IsNullOrWhiteSpace(c.Region) ? "—" : c.Region)}\n" +
                $"Notes: {(string.IsNullOrWhiteSpace(c.Notes) ? "—" : c.Notes)}\n" +
                $"Added: {c.AddedAtUtc:yyyy-MM-dd} (by <@{c.AddedByDiscordUserId}>)"
            );
        }

        private static async Task List(SocketMessage message)
        {
            var all = BotStores.Creators.LoadAll()
                .OrderBy(c => CreatorStore.NormalizeHandle(c.Handle))
                .ToList();

            if (all.Count == 0)
            {
                await message.Channel.SendMessageAsync("No creators saved yet. Use `!creator add ...`");
                return;
            }

            var lines = all.Take(50).Select(c => $"{c.Handle} — {c.DisplayName}");
            var msg = "**Creators (first 50):**\n" + string.Join("\n", lines);

            if (all.Count > 50)
                msg += $"\n…and {all.Count - 50} more.";

            await message.Channel.SendMessageAsync(msg);
        }

        private static async Task Add(SocketMessage message, SocketGuildChannel gch, string[] args)
        {
            if (!IsAdmin(gch, message.Author))
            {
                await message.Channel.SendMessageAsync("🚫 Admins only.");
                return;
            }

            // !creator add @handle "Display Name" uid=123 region=UK notes="..."
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: `!creator add <@handle> <displayName> [uid=] [region=] [notes=]`");
                return;
            }

            var handle = CreatorStore.NormalizeHandle(args[0]);
            var displayName = args[1].Trim('"');

            string uid = "";
            string region = "";
            string notes = "";

            foreach (var a in args.Skip(2))
            {
                if (a.StartsWith("uid=", StringComparison.OrdinalIgnoreCase))
                    uid = a.Substring(4).Trim('"');
                else if (a.StartsWith("region=", StringComparison.OrdinalIgnoreCase))
                    region = a.Substring(7).Trim('"');
                else if (a.StartsWith("notes=", StringComparison.OrdinalIgnoreCase))
                    notes = a.Substring(6).Trim('"');
            }

            var all = BotStores.Creators.LoadAll();
            if (all.Any(c => CreatorStore.NormalizeHandle(c.Handle) == handle))
            {
                await message.Channel.SendMessageAsync($"That handle already exists: {handle}");
                return;
            }

            all.Add(new CreatorProfile
            {
                Handle = handle,
                DisplayName = displayName,
                TiktokUid = uid,
                Region = region,
                Notes = notes,
                AddedByDiscordUserId = message.Author.Id,
                AddedAtUtc = DateTimeOffset.UtcNow
            });

            BotStores.Creators.SaveAll(all);
            await message.Channel.SendMessageAsync($"✅ Added {handle} (**{displayName}**).");
        }

        private static async Task Remove(SocketMessage message, SocketGuildChannel gch, string[] args)
        {
            if (!IsAdmin(gch, message.Author))
            {
                await message.Channel.SendMessageAsync("🚫 Admins only.");
                return;
            }

            if (args.Length < 1)
            {
                await message.Channel.SendMessageAsync("Usage: `!creator remove <@handle>`");
                return;
            }

            var handle = CreatorStore.NormalizeHandle(args[0]);
            var all = BotStores.Creators.LoadAll();
            var removed = all.RemoveAll(c => CreatorStore.NormalizeHandle(c.Handle) == handle);

            if (removed == 0)
            {
                await message.Channel.SendMessageAsync($"No match for {handle}.");
                return;
            }

            BotStores.Creators.SaveAll(all);
            await message.Channel.SendMessageAsync($"✅ Removed {handle}.");
        }
    }
}