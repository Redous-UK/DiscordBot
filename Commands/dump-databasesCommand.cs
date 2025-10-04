using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;

namespace MyDiscordBot.Commands
{
    public class DumpDatabasesCommand : ILegacyCommand
    {
        public string Name => "dump-databases";
        public string Description => "Admin only. Dumps the bot's JSON databases. Usage: !dump-databases [raw|ls|where] [files...]";

        public string Category => "🛠️ Moderation";

        // Add/trim file names to match your project
        private static readonly string[] DefaultDbFiles =
        [
            "reminders.json",
            "birthdays.json",
            "guildsettings.json",
            "users.json"
        ];

        private const int MaxChunk = 1900;

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketTextChannel channel)
            {
                await message.Channel.SendMessageAsync("This command must be run in a server.");
                return;
            }

            var member = channel.GetUser(message.Author.Id);
            if (member == null || !member.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("🚫 You need Administrator permissions to run this command.");
                return;
            }

            var mode = args.FirstOrDefault()?.ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            switch (mode)
            {
                case "ls":
                    await ListFiles(channel);
                    return;
                case "where":
                    await ShowWhere(channel);
                    return;
                case "raw":
                    await Dump(rawMode: true, channel, rest);
                    return;
                default:
                    // No subcommand: treat entire args as filenames (or default set)
                    await Dump(rawMode: false, channel, args);
                    return;
            }
        }

        private static async Task Dump(bool rawMode, SocketTextChannel channel, string[] fileArgs)
        {
            var requested = fileArgs?.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray() ?? Array.Empty<string>();
            string[] files = (requested.Length > 0 ? requested : DefaultDbFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                await channel.SendMessageAsync("No files specified.");
                return;
            }

            var candidates = GetCandidateDirs();
            var resolved = ResolveExistingFiles(files, candidates);

            if (resolved.Count == 0)
            {
                await channel.SendMessageAsync(
                    "❓ I couldn’t find any of the requested files.\n" +
                    "Try `!dump-databases where` to see the directories I’m checking, " +
                    "and `!dump-databases ls` to list what’s actually there.");
                return;
            }

            if (rawMode)
            {
                await SendRawFiles(channel, resolved);
            }
            else
            {
                await SendReadableEmbeds(channel, resolved);
            }

            // Mention any that were missing
            var missing = files.Where(f => !resolved.Keys.Any(k => Path.GetFileName(k).Equals(f, StringComparison.OrdinalIgnoreCase))).ToList();
            if (missing.Count > 0)
            {
                await channel.SendMessageAsync("ℹ️ Not found: " + string.Join(", ", missing.Select(m => $"`{m}`")));
            }
        }

        private static async Task ListFiles(SocketTextChannel channel)
        {
            var sb = new StringBuilder();
            var dirs = GetCandidateDirs().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var d in dirs)
            {
                try
                {
                    if (Directory.Exists(d))
                    {
                        var files = Directory.EnumerateFiles(d, "*.json", SearchOption.TopDirectoryOnly)
                                             .Select(Path.GetFileName)
                                             .OrderBy(n => n)
                                             .ToList();
                        sb.AppendLine($"**{d}**");
                        if (files.Count == 0) sb.AppendLine("_(no .json files)_");
                        else sb.AppendLine(string.Join(", ", files.Select(f => $"`{f}`")));
                        sb.AppendLine();
                    }
                }
                catch { /* ignore directory errors */ }
            }

            if (sb.Length == 0) sb.AppendLine("_No candidate directories found or accessible._");

            foreach (var chunk in ChunkForCodeFence(sb.ToString(), MaxChunk))
                await channel.SendMessageAsync(chunk);
        }

        private static async Task ShowWhere(SocketTextChannel channel)
        {
            string working = Directory.GetCurrentDirectory();
            string baseDir = AppContext.BaseDirectory ?? "(null)";
            var envDataDir = Environment.GetEnvironmentVariable("DATA_DIR");
            var envRemStore = Environment.GetEnvironmentVariable("REMINDER_STORE_PATH");
            var envBotData = Environment.GetEnvironmentVariable("BOT_DATA_DIR");

            var dirs = GetCandidateDirs();

            var msg =
$@"**Paths I will check**
- Working dir: `{working}`
- App base dir: `{baseDir}`
- DATA_DIR: `{envDataDir ?? "(unset)"}`
- REMINDER_STORE_PATH: `{envRemStore ?? "(unset)"}`
- BOT_DATA_DIR: `{envBotData ?? "(unset)"}`
- Common mounts: `/var/data`, `/data`, `./data`

**Search order**
{string.Join("\n", dirs.Select(d => "- `" + d + "`"))}

Tip: On Render, set a Disk mount path (e.g., `/var/data`) and set `DATA_DIR` to that path. Save your JSON there.";
            foreach (var chunk in ChunkForCodeFence(msg, MaxChunk))
                await channel.SendMessageAsync(chunk);
        }

        // ---------- File Sending ----------

        private static async Task SendRawFiles(SocketTextChannel channel, Dictionary<string, string> fullPathsByName)
        {
            foreach (var kvp in fullPathsByName)
            {
                var fullPath = kvp.Value;
                try
                {
                    await channel.SendFileAsync(fullPath, $"📦 `{Path.GetFileName(fullPath)}`");
                }
                catch (Exception ex)
                {
                    await channel.SendMessageAsync($"⚠️ Could not send `{fullPath}`: {ex.Message}");
                }
            }
        }

        private static async Task SendReadableEmbeds(SocketTextChannel channel, Dictionary<string, string> fullPathsByName)
        {
            foreach (var kvp in fullPathsByName)
            {
                var name = Path.GetFileName(kvp.Key);
                var path = kvp.Value;
                string pretty;
                long sizeBytes = 0;

                try
                {
                    var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
                    sizeBytes = new FileInfo(path).Length;
                    pretty = TryPrettyPrint(json, out var error)
                        ? Pretty(json)
                        : $"/* Invalid JSON (showing raw) - {error} */\n" + json;
                }
                catch (Exception ex)
                {
                    await channel.SendMessageAsync($"⚠️ Could not read `{path}`: {ex.Message}");
                    continue;
                }

                var embed = new EmbedBuilder()
                    .WithTitle($"📄 {name}")
                    .WithDescription($"Path: `{path}`\nSize: `{sizeBytes:n0}` bytes\nMode: `embed` (pretty-printed; long files are split).")
                    .WithColor(new Color(70, 130, 180))
                    .WithCurrentTimestamp()
                    .Build();
                await channel.SendMessageAsync(embed: embed);

                foreach (var chunk in ChunkForCodeFence(pretty, MaxChunk))
                    await channel.SendMessageAsync($"```json\n{chunk}\n```");
            }
        }

        // ---------- Helpers ----------

        private static Dictionary<string, string> ResolveExistingFiles(IEnumerable<string> names, IEnumerable<string> searchDirs)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                // If they provided an absolute/relative path that already exists, use it directly.
                if (File.Exists(n))
                {
                    result[n] = Path.GetFullPath(n);
                    continue;
                }

                foreach (var dir in searchDirs)
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        var full = Path.Combine(dir, n);
                        if (File.Exists(full))
                        {
                            result[n] = full;
                            break;
                        }
                    }
                    catch { /* ignore path errors */ }
                }
            }
            return result;
        }

        private static IEnumerable<string> GetCandidateDirs()
        {
            var dirs = new List<string>();

            // Highest priority: env vars you control
            var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
            var remPath = Environment.GetEnvironmentVariable("REMINDER_STORE_PATH");
            var botData = Environment.GetEnvironmentVariable("BOT_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(dataDir)) dirs.Add(dataDir);
            if (!string.IsNullOrWhiteSpace(remPath)) dirs.Add(remPath);
            if (!string.IsNullOrWhiteSpace(botData)) dirs.Add(botData);

            // Common Render disk mounts
            dirs.Add("/var/data");
            dirs.Add("/data");

            // App dirs
            dirs.Add(Directory.GetCurrentDirectory());
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir)) dirs.Add(baseDir);

            // Likely subfolders
            dirs.Add(Path.Combine(Directory.GetCurrentDirectory(), "data"));
            if (!string.IsNullOrWhiteSpace(baseDir)) dirs.Add(Path.Combine(baseDir, "data"));

            // Deduplicate while preserving order
            return dirs.Where(d => !string.IsNullOrWhiteSpace(d))
                       .Select(d => d.TrimEnd('/', '\\'))
                       .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryPrettyPrint(string json, out string error)
        {
            try
            {
                using var _ = JsonDocument.Parse(json);
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string Pretty(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return JsonSerializer.Serialize(doc.RootElement, options);
        }

        private static IEnumerable<string> ChunkForCodeFence(string text, int maxChunk)
        {
            if (string.IsNullOrEmpty(text)) { yield return ""; yield break; }
            int i = 0;
            while (i < text.Length)
            {
                int len = Math.Min(maxChunk, text.Length - i);
                int lastNewline = text.LastIndexOf('\n', i + len - 1, len);
                if (lastNewline <= i || lastNewline == -1)
                {
                    yield return text.Substring(i, len);
                    i += len;
                }
                else
                {
                    int take = lastNewline - i + 1;
                    yield return text.Substring(i, take);
                    i += take;
                }
            }
        }
    }
}