using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;

namespace YourBot.Commands
{
    public class DumpDatabasesCommand : ILegacyCommand
    {
        public string Name => "dump-databases";
        public string Description => "Admin only. Dumps the bot's JSON databases. Usage: !dump-databases [raw] [files...]";
        public string Category => "🛠️ Moderation";

        // Add any new database files here if you create more later.
        private static readonly string[] DefaultDbFiles = new[]
        {
            "reminders.json",
            "birthdays.json",
            "guildsettings.json",
            "users.json"
        };

        // Discord message hard limit is 2000; leave room for code fences & labels.
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

            // Parse args
            bool rawMode = args.Any(a => string.Equals(a, "raw", StringComparison.OrdinalIgnoreCase));
            var requestedFiles = args.Where(a => !string.Equals(a, "raw", StringComparison.OrdinalIgnoreCase))
                                     .ToArray();

            string[] files = (requestedFiles.Length > 0 ? requestedFiles : DefaultDbFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                await message.Channel.SendMessageAsync("No files specified.");
                return;
            }

            if (rawMode)
            {
                await SendRawFiles(channel, files);
            }
            else
            {
                await SendReadableEmbeds(channel, files);
            }
        }

        private static async Task SendRawFiles(SocketTextChannel channel, IEnumerable<string> files)
        {
            var missing = new List<string>();
            var sentAny = false;

            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    missing.Add(file);
                    continue;
                }

                try
                {
                    // Discord supports multiple attachments, but we’ll send one-by-one for simple error reporting.
                    await channel.SendFileAsync(file, $"📦 `{file}`");
                    sentAny = true;
                }
                catch (Exception ex)
                {
                    await channel.SendMessageAsync($"⚠️ Could not send `{file}`: {ex.Message}");
                }
            }

            if (missing.Count > 0)
            {
                await channel.SendMessageAsync("ℹ️ Not found: " + string.Join(", ", missing.Select(m => $"`{m}`")));
            }

            if (!sentAny && missing.Count > 0)
            {
                await channel.SendMessageAsync("No files were sent. Double-check the filenames or working directory.");
            }
        }

        private static async Task SendReadableEmbeds(SocketTextChannel channel, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    await channel.SendMessageAsync($"ℹ️ `{file}` not found.");
                    continue;
                }

                string pretty;
                long sizeBytes = 0;

                try
                {
                    var json = await File.ReadAllTextAsync(file, Encoding.UTF8);
                    sizeBytes = new FileInfo(file).Length;
                    pretty = TryPrettyPrint(json, out var error)
                        ? TryPrettyPrintReturn(json)
                        : $"/* Invalid JSON (showing raw) - {error} */\n" + json;
                }
                catch (Exception ex)
                {
                    await channel.SendMessageAsync($"⚠️ Could not read `{file}`: {ex.Message}");
                    continue;
                }

                // Build a compact header embed first
                var embed = new EmbedBuilder()
                    .WithTitle($"📄 {file}")
                    .WithDescription($"Size: `{sizeBytes:n0}` bytes\nMode: `embed` (pretty-printed; long files are split across messages)\nTip: use `!dump-databases raw {file}` to download.")
                    .WithColor(new Color(70, 130, 180))
                    .WithCurrentTimestamp()
                    .Build();

                await channel.SendMessageAsync(embed: embed);

                // Chunk pretty JSON inside code fences, keeping each message <= 2000 chars
                foreach (var chunk in ChunkForCodeFence(pretty, MaxChunk))
                {
                    await channel.SendMessageAsync($"```json\n{chunk}\n```");
                }
            }
        }

        private static bool TryPrettyPrint(string json, out string error)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string TryPrettyPrintReturn(string json)
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
            // Ensure we don’t break inside a code fence header; just slice the raw text.
            if (string.IsNullOrEmpty(text))
            {
                yield return "";
                yield break;
            }

            int idx = 0;
            while (idx < text.Length)
            {
                int len = Math.Min(maxChunk, text.Length - idx);
                // Try to break at a newline when possible to keep it tidy
                int lastNewline = text.LastIndexOf('\n', idx + len - 1, len);
                if (lastNewline <= idx || lastNewline == -1)
                {
                    yield return text.Substring(idx, len);
                    idx += len;
                }
                else
                {
                    int take = lastNewline - idx + 1;
                    yield return text.Substring(idx, take);
                    idx += take;
                }
            }
        }
    }
}