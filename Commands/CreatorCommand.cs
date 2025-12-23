using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MyDiscordBot.Guards;

namespace MyDiscordBot.Commands
{
    // -------------------- Agency Guild Guard --------------------
    internal static class GuildGuards
    {
        private static readonly HashSet<ulong> AgencyGuildIds = LoadAgencyGuildIds();

        public static bool IsAgencyGuild(ulong guildId)
            => AgencyGuildIds.Count > 0 && AgencyGuildIds.Contains(guildId);

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

    // -------------------- Models --------------------
    internal sealed class CreatorProfile
    {
        public string Handle { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string TiktokUid { get; set; } = "";
        public string Region { get; set; } = "";
        public string Notes { get; set; } = "";

        public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public ulong AddedByDiscordUserId { get; set; }
    }

    // -------------------- Store --------------------
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
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            if (!File.Exists(_path))
                File.WriteAllText(_path, "[]");
        }

        public List<CreatorProfile> LoadAll()
        {
            lock (_lock)
            {
                return JsonSerializer.Deserialize<List<CreatorProfile>>(
                           File.ReadAllText(_path), JsonOpts)
                       ?? new List<CreatorProfile>();
            }
        }

        public void SaveAll(List<CreatorProfile> creators)
        {
            lock (_lock)
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(creators, JsonOpts));
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
            handle = handle.Trim();
            if (!handle.StartsWith("@")) handle = "@" + handle;
            return handle.ToLowerInvariant();
        }
    }

    internal static class BotStores
    {
        public static readonly CreatorStore Creators =
            new CreatorStore(Environment.GetEnvironmentVariable("CREATOR_STORE_PATH") ?? "data/creators.json");
    }

    // -------------------- Command --------------------
    public class CreatorCommand : ILegacyCommand
    {
        public string Name => "creator";
        public string Description => "Agency creator management commands.";
        public string Category => "⚙️ Settings & Config";
        public string Usage =>
            "!creator info <@handle> | !creator list | !creator add <@handle> <displayName> [uid=] [region=] [notes=] | !creator remove <@handle>";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel gch)
            {
                await message.Channel.SendMessageAsync("❌ This command can only be used in a server.");
                return;
            }

            if (!GuildGuards.IsAgencyGuild(gch.Guild.Id))
            {
                await message.Channel.SendMessageAsync("❌ This is not an agency guild.");
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

        private static bool IsAdmin(SocketGuildChannel gch, SocketUser user)
        {
            var member = gch.GetUser(user.Id);
            return member != null && member.GuildPermissions.Administrator;
        }

        // -------------------- Subcommands --------------------

        private static async Task Info(SocketMessage message, string[] args)
        {
            if (args.Length < 1)
            {
                await message.Channel.SendMessageAsync("Usage: `!creator info <@handle>`");
                return;
            }

            var c = BotStores.Creators.FindByHandle(args[0]);
            if (c == null)
            {
                await message.Channel.SendMessageAsync("❌ Creator not found.");
                return;
            }

            await message.Channel.SendMessageAsync(
                $"**{c.DisplayName}** ({c.Handle})\n" +
                $"UID: {(string.IsNullOrWhiteSpace(c.TiktokUid) ? "—" : c.TiktokUid)}\n" +
                $"Region: {(string.IsNullOrWhiteSpace(c.Region) ? "—" : c.Region)}\n" +
                $"Notes: {(string.IsNullOrWhiteSpace(c.Notes) ? "—" : c.Notes)}\n" +
                $"Added: {c.AddedAtUtc:yyyy-MM-dd} by <@{c.AddedByDiscordUserId}>"
            );
        }

        private static async Task List(SocketMessage message)
        {
            var all = BotStores.Creators.LoadAll()
                .OrderBy(c => c.Handle)
                .ToList();

            if (all.Count == 0)
            {
                await message.Channel.SendMessageAsync("No creators saved.");
                return;
            }

            var lines = all.Take(50).Select(c => $"{c.Handle} — {c.DisplayName}");
            var text = "**Creators:**\n" + string.Join("\n", lines);

            if (all.Count > 50)
                text += $"\n…and {all.Count - 50} more.";

            await message.Channel.SendMessageAsync(text);
        }

        private static async Task Add(SocketMessage message, SocketGuildChannel gch, string[] args)
        {
            if (!IsAdmin(gch, message.Author))
            {
                await message.Channel.SendMessageAsync("🚫 Admins only.");
                return;
            }

            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync(
                    "Usage: `!creator add <@handle> <displayName> [uid=] [region=] [notes=]`");
                return;
            }

            var handle = CreatorStore.NormalizeHandle(args[0]);
            var displayName = args[1].Trim('"');

            var uid = "";
            var region = "";
            var notes = "";

            foreach (var a in args.Skip(2))
            {
                if (a.StartsWith("uid=", StringComparison.OrdinalIgnoreCase))
                    uid = a[4..];
                else if (a.StartsWith("region=", StringComparison.OrdinalIgnoreCase))
                    region = a[7..];
                else if (a.StartsWith("notes=", StringComparison.OrdinalIgnoreCase))
                    notes = a[6..].Trim('"');
            }

            var all = BotStores.Creators.LoadAll();
            if (all.Any(c => CreatorStore.NormalizeHandle(c.Handle) == handle))
            {
                await message.Channel.SendMessageAsync("❌ That creator already exists.");
                return;
            }

            all.Add(new CreatorProfile
            {
                Handle = handle,
                DisplayName = displayName,
                TiktokUid = uid,
                Region = region,
                Notes = notes,
                AddedByDiscordUserId = message.Author.Id
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
                await message.Channel.SendMessageAsync("❌ Creator not found.");
                return;
            }

            BotStores.Creators.SaveAll(all);
            await message.Channel.SendMessageAsync($"✅ Removed {handle}.");
        }
    }
}