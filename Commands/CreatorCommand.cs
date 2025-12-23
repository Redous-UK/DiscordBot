using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MyDiscordBot.Guards;

namespace MyDiscordBot.Commands
{
    // -------------------- Models --------------------
    internal sealed class CreatorProfile
    {
        public string Handle { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string TiktokUid { get; set; } = "";
        public string Diamonds { get; set; } = "";
        public string GoLiveDays { get; set; } = "";
        public string Manager { get; set; } = "";
        public string Promote { get; set; } = "";
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
            "!creator info <@handle> | !creator list | !creator add <@handle> <displayName> [uid=] [goLiveDays=] [Manager=] [Promote=] [notes=] | !creator remove <@handle> | !creator import (attach CSV)";
        
        private static readonly HttpClient _http = new HttpClient();

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel gch)
            {
                await message.Channel.SendMessageAsync("❌ This command can only be used in a server.");
                return;
            }

            // ✅ Uses your shared guard: MyDiscordBot.Guards.GuildGuards
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

                case "import":
                    await ImportCsv(message, gch);
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
                $"Diamonds: {(string.IsNullOrWhiteSpace(c.Diamonds) ? "—" : c.Diamonds)}\n" +
                $"GoLiveDays: {(string.IsNullOrWhiteSpace(c.GoLiveDays) ? "—" : c.GoLiveDays)}\n" +
                $"Manager: {(string.IsNullOrWhiteSpace(c.Manager) ? "—" : c.Manager)}\n" +
                $"Promote: {(string.IsNullOrWhiteSpace(c.Promote) ? "—" : c.Promote)}\n" +
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
                    "Usage: `!creator add <@handle> <displayName> [uid=] [goLiveDays=] [Manager=] [Promote=] [notes=]`");
                return;
            }

            var handle = CreatorStore.NormalizeHandle(args[0]);
            var displayName = args[1].Trim('"');

            var uid = "";
            var diamonds = "";
            var goLiveDays = "";
            var manager = "";
            var promote = "";
            var notes = "";

            foreach (var a in args.Skip(2))
            {
                if (a.StartsWith("uid=", StringComparison.OrdinalIgnoreCase))
                    uid = a[4..];
                else if (a.StartsWith("diamonds=", StringComparison.OrdinalIgnoreCase))
                    diamonds = a[7..];
                else if (a.StartsWith("golivedays=", StringComparison.OrdinalIgnoreCase))
                    goLiveDays = a[12..];
                else if (a.StartsWith("manager=", StringComparison.OrdinalIgnoreCase))
                    manager = a[8..].Trim('"');
                else if (a.StartsWith("promote=", StringComparison.OrdinalIgnoreCase))
                    promote = a[8..].Trim('"');
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
                Diamonds = diamonds,
                GoLiveDays = goLiveDays,
                Manager = manager,
                Promote = promote,
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
                await message.Channel.SendMessageAsync("❌ Creator not found.");
                return;
            }

            BotStores.Creators.SaveAll(all);
            await message.Channel.SendMessageAsync($"✅ Removed {handle}.");
        }

        // -------------------- CSV Import --------------------

        private static async Task ImportCsv(SocketMessage message, SocketGuildChannel gch)
        {
            if (!IsAdmin(gch, message.Author))
            {
                await message.Channel.SendMessageAsync("🚫 Admins only.");
                return;
            }

            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment == null)
            {
                await message.Channel.SendMessageAsync(
                    "Attach a CSV file to the same message.\n" +
                    "Usage: `!creator import` (with a `.csv` attached)\n\n" +
                    "Required header: `handle`\n" +
                    "Optional: `displayName,tiktokUid,diamonds,GoLiveDays,Manager,Promote,notes`");
                return;
            }

            var fileName = attachment.Filename ?? "upload.csv";
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync("❌ Please attach a `.csv` file.");
                return;
            }

            string csvText;
            try
            {
                csvText = await _http.GetStringAsync(attachment.Url);
            }
            catch (Exception ex)
            {
                await message.Channel.SendMessageAsync($"❌ Failed to download CSV: {ex.Message}");
                return;
            }

            if (!TryParseCreatorsCsv(csvText, out var rows, out var error))
            {
                await message.Channel.SendMessageAsync($"❌ CSV parse error: {error}");
                return;
            }

            if (rows.Count == 0)
            {
                await message.Channel.SendMessageAsync("CSV contained no rows to import.");
                return;
            }

            var store = BotStores.Creators;
            var all = store.LoadAll();

            int added = 0, updated = 0, skipped = 0;

            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Handle))
                {
                    skipped++;
                    continue;
                }

                var norm = CreatorStore.NormalizeHandle(r.Handle);
                var existing = all.FirstOrDefault(c => CreatorStore.NormalizeHandle(c.Handle) == norm);

                if (existing == null)
                {
                    all.Add(new CreatorProfile
                    {
                        Handle = norm,
                        DisplayName = r.DisplayName ?? "",
                        TiktokUid = r.TiktokUid ?? "",
                        Diamonds = r.Diamonds ?? "",
                        GoLiveDays = r.GoLiveDays ?? "",
                        Manager = r.Manager ?? "",
                        Promote = r.Promote ?? "",
                        Notes = r.Notes ?? "",
                        AddedAtUtc = DateTimeOffset.UtcNow,
                        AddedByDiscordUserId = message.Author.Id
                    });
                    added++;
                }
                else
                {
                    // Don't overwrite with blanks
                    if (!string.IsNullOrWhiteSpace(r.DisplayName)) existing.DisplayName = r.DisplayName!;
                    if (!string.IsNullOrWhiteSpace(r.TiktokUid)) existing.TiktokUid = r.TiktokUid!;
                    if (!string.IsNullOrWhiteSpace(r.Diamonds)) existing.Diamonds = r.Diamonds!;
                    if (!string.IsNullOrWhiteSpace(r.GoLiveDays)) existing.GoLiveDays = r.GoLiveDays!;
                    if (!string.IsNullOrWhiteSpace(r.Manager)) existing.Manager = r.Manager!;
                    if (!string.IsNullOrWhiteSpace(r.Promote)) existing.Promote = r.Promote!;
                    if (!string.IsNullOrWhiteSpace(r.Notes)) existing.Notes = r.Notes!;

                    updated++;
                }
            }

            store.SaveAll(all);

            await message.Channel.SendMessageAsync(
                $"✅ Import complete from `{fileName}`\n" +
                $"- Added: **{added}**\n" +
                $"- Updated: **{updated}**\n" +
                $"- Skipped: **{skipped}**\n" +
                $"- Total creators now: **{all.Count}**"
            );
        }

        private sealed class ImportRow
        {
            public string Handle { get; set; } = "";
            public string? DisplayName { get; set; }
            public string? TiktokUid { get; set; }
            public string? Diamonds { get; set; }
            public string? GoLiveDays { get; set; }
            public string? Manager { get; set; }
            public string? Promote { get; set; }
            public string? Notes { get; set; }
        }

        private static bool TryParseCreatorsCsv(string csv, out List<ImportRow> rows, out string error)
        {
            rows = new List<ImportRow>();
            error = "";

            var lines = ReadCsvLines(csv).Where(l => l != null).ToList();
            if (lines.Count == 0)
            {
                error = "Empty file.";
                return false;
            }

            var header = SplitCsvLine(lines[0]);
            if (header.Count == 0)
            {
                error = "Missing header row.";
                return false;
            }

            int Idx(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

            var iHandle = Idx("Creator's username");
            if (iHandle < 0)
            {
                error = "Header must include a `handle` column.";
                return false;
            }

            var iDisplay = Idx("Creator's username");
            var iUid = Idx("Creator ID:");
            var iDiamonds = Idx("Diamonds in L30D");
            var iGoLive = Idx("Valid go LIVE days in L30D");
            var iManager = Idx("Creator Network manager");
            var iPromote = Idx("Promote permission");
            var iNotes = Idx("Notes");

            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var cols = SplitCsvLine(lines[i]);

                string Get(int idx) => (idx >= 0 && idx < cols.Count) ? cols[idx] : "";

                var handle = Get(iHandle).Trim();
                if (string.IsNullOrWhiteSpace(handle)) continue;

                rows.Add(new ImportRow
                {
                    Handle = handle,
                    DisplayName = NullIfBlank(Get(iDisplay)),
                    TiktokUid = NullIfBlank(Get(iUid)),
                    Diamonds = NullIfBlank(Get(iDiamonds)),
                    GoLiveDays = NullIfBlank(Get(iGoLive)),
                    Manager = NullIfBlank(Get(iManager)),
                    Promote = NullIfBlank(Get(iPromote)),
                    Notes = NullIfBlank(Get(iNotes)),
                });
            }

            return true;
        }

        private static string? NullIfBlank(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static IEnumerable<string> ReadCsvLines(string csv)
        {
            using var sr = new StringReader(csv);
            string? line;
            while ((line = sr.ReadLine()) != null)
                yield return line;
        }

        // Simple CSV splitter: quoted fields + escaped quotes ("")
        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString().Trim());
            return result;
        }
    }
}