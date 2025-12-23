// Commands/CreatorCommand.cs
// Agency creator command (legacy ILegacyCommand style) restricted to one guild via AGENCY_GUILD_ID.
// Stores creator profiles in a JSON file (CREATOR_STORE_PATH or data/creators.json).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Discord;
// using Discord.WebSocket;

namespace MyDiscordBot.Commands
{
    // ---------- Guild Lock Helper ----------
    internal static class GuildGuards
    {
        public static ulong AgencyGuildId { get; } =
            ulong.TryParse(Environment.GetEnvironmentVariable("AGENCY_GUILD_ID"), out var id) ? id : 0;

        public static bool IsAgencyGuild(ulong guildId)
            => AgencyGuildId != 0 && guildId == AgencyGuildId;
    }

    // ---------- Model ----------
    internal class CreatorProfile
    {
        public string Handle { get; set; } = "";      // e.g. @jenna
        public string DisplayName { get; set; } = ""; // e.g. Jenna
        public string TiktokUid { get; set; } = "";   // optional
        public string Region { get; set; } = "";      // optional
        public string Notes { get; set; } = "";       // internal notes

        public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public ulong AddedByDiscordUserId { get; set; }
    }

    // ---------- JSON Store ----------
    internal class CreatorStore
    {
        private readonly string _path;
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public CreatorStore(string path)
        {
            _path = path;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_path))
                File.WriteAllText(_path, "[/data/creators.json]");
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

                // Backup existing file
                if (File.Exists(_path))
                    File.Copy(_path, bak, overwrite: true);

                // Atomic-ish replace
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

    // ---------- Bot Store Singleton ----------
    internal static class BotStores
    {
        public static readonly CreatorStore Creators =
            new CreatorStore(Environment.GetEnvironmentVariable("CREATOR_STORE_PATH") ?? "data/creators.json");
    }

    // ---------- Command ----------
    // Assumes you have:
    // interface ILegacyCommand { string Name {get;} string Description {get;} string Usage {get;} Task ExecuteAsync(LegacyCommandContext context); }
    // and LegacyCommandContext has at least:
    // - Guild (nullable) with Id
    // - Channel with SendMessageAsync(string)
    // - User with Id
    // - Args string[]
    // - A boolean permission helper like UserHasAdministrator (or replace IsAdmin logic)
    public class CreatorCommand : ILegacyCommand
    {
        public string Name => "creator";
        public string Description => "Agency creator commands (info/list/add/remove).";

        public string Category => "🛠️ Agency Management";
        public string Usage => "!creator info <handle> | !creator list | !creator add <handle> <displayName> [uid=] [region=] [notes=] | !creator remove <handle>";

        private readonly CreatorStore _store;

        // Default constructor for auto-discovery systems using Activator.CreateInstance
        public CreatorCommand() : this(BotStores.Creators) { }

        public CreatorCommand(CreatorStore store)
        {
            _store = store;
        }

        public async Task ExecuteAsync(LegacyCommandContext context)
        {
            // ✅ Hard lock to agency guild only
            if (context.Guild == null || !GuildGuards.IsAgencyGuild(context.Guild.Id))
            {
                await context.Channel.SendMessageAsync("❌ Agency commands are only available in the agency server.");
                return;
            }

            var args = context.Args ?? Array.Empty<string>();
            if (args.Length == 0)
            {
                await context.Channel.SendMessageAsync($"Usage: {Usage}");
                return;
            }

            var sub = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            switch (sub)
            {
                case "info":
                    await Info(context, rest);
                    break;

                case "list":
                    await List(context);
                    break;

                case "add":
                    await Add(context, rest);
                    break;

                case "remove":
                case "delete":
                    await Remove(context, rest);
                    break;

                default:
                    await context.Channel.SendMessageAsync($"Unknown subcommand `{sub}`.\nUsage: {Usage}");
                    break;
            }
        }

        private async Task Info(LegacyCommandContext ctx, string[] args)
        {
            if (args.Length < 1)
            {
                await ctx.Channel.SendMessageAsync("Usage: !creator info <handle>");
                return;
            }

            var handle = args[0];
            var c = _store.FindByHandle(handle);

            if (c == null)
            {
                await ctx.Channel.SendMessageAsync($"Couldn’t find {CreatorStore.NormalizeHandle(handle)}.");
                return;
            }

            await ctx.Channel.SendMessageAsync(
                $"**{c.DisplayName}** ({c.Handle})\n" +
                $"UID: {(string.IsNullOrWhiteSpace(c.TiktokUid) ? "—" : c.TiktokUid)}\n" +
                $"Region: {(string.IsNullOrWhiteSpace(c.Region) ? "—" : c.Region)}\n" +
                $"Notes: {(string.IsNullOrWhiteSpace(c.Notes) ? "—" : c.Notes)}\n" +
                $"Added: {c.AddedAtUtc:yyyy-MM-dd} (by <@{c.AddedByDiscordUserId}>)"
            );
        }

        private async Task List(LegacyCommandContext ctx)
        {
            var all = _store.LoadAll()
                .OrderBy(c => CreatorStore.NormalizeHandle(c.Handle))
                .ToList();

            if (all.Count == 0)
            {
                await ctx.Channel.SendMessageAsync("No creators saved yet. Use `!creator add ...`");
                return;
            }

            // Keep message length safe:
            var lines = all.Take(50).Select(c => $"{c.Handle} — {c.DisplayName}");
            var msg = "**Creators (first 50):**\n" + string.Join("\n", lines);

            if (all.Count > 50)
                msg += $"\n…and {all.Count - 50} more.";

            await ctx.Channel.SendMessageAsync(msg);
        }

        private bool IsAdmin(LegacyCommandContext ctx)
        {
            // ✅ Replace with your actual permission check.
            // Common patterns in your bot:
            // - ctx.UserHasAdministrator
            // - ctx.HasPermission(GuildPermission.Administrator)
            // - ((SocketGuildUser)ctx.User).GuildPermissions.Administrator

            return ctx.UserHasAdministrator;
        }

        private async Task Add(LegacyCommandContext ctx, string[] args)
        {
            if (!IsAdmin(ctx))
            {
                await ctx.Channel.SendMessageAsync("❌ Admin only.");
                return;
            }

            // Example:
            // !creator add @handle "Display Name" uid=123 region=UK notes="Great engagement"
            if (args.Length < 2)
            {
                await ctx.Channel.SendMessageAsync("Usage: !creator add <handle> <displayName> [uid=] [region=] [notes=]");
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

            var all = _store.LoadAll();
            if (all.Any(c => CreatorStore.NormalizeHandle(c.Handle) == handle))
            {
                await ctx.Channel.SendMessageAsync($"That handle already exists: {handle}");
                return;
            }

            all.Add(new CreatorProfile
            {
                Handle = handle,
                DisplayName = displayName,
                TiktokUid = uid,
                Region = region,
                Notes = notes,
                AddedByDiscordUserId = ctx.User.Id,
                AddedAtUtc = DateTimeOffset.UtcNow
            });

            _store.SaveAll(all);
            await ctx.Channel.SendMessageAsync($"✅ Added {handle} (**{displayName}**).");
        }

        private async Task Remove(LegacyCommandContext ctx, string[] args)
        {
            if (!IsAdmin(ctx))
            {
                await ctx.Channel.SendMessageAsync("❌ Admin only.");
                return;
            }

            if (args.Length < 1)
            {
                await ctx.Channel.SendMessageAsync("Usage: !creator remove <handle>");
                return;
            }

            var handle = CreatorStore.NormalizeHandle(args[0]);
            var all = _store.LoadAll();
            var removed = all.RemoveAll(c => CreatorStore.NormalizeHandle(c.Handle) == handle);

            if (removed == 0)
            {
                await ctx.Channel.SendMessageAsync($"No match for {handle}.");
                return;
            }

            _store.SaveAll(all);
            await ctx.Channel.SendMessageAsync($"✅ Removed {handle}.");
        }
    }

    // ---------- Placeholder Interfaces (REMOVE if you already have these) ----------
    // These are ONLY here so the file is copy-paste complete. Delete them if your project already defines them.

    public class LegacyCommandContext
    {
        public dynamic? Guild { get; set; }          // expects Guild.Id
        public dynamic Channel { get; set; } = default!; // expects SendMessageAsync(string)
        public dynamic User { get; set; } = default!;    // expects User.Id
        public string[] Args { get; set; } = Array.Empty<string>();

        // Replace this with your real permission signal
        public bool UserHasAdministrator { get; set; }
    }
}