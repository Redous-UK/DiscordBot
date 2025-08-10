using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;

namespace MyDiscordBot.Commands
{
    public class HelpCommand : ILegacyCommand
    {
        public string Name => "help";
        public string Description => "List commands by category, or `!help <command>` for details.";
        public string Category => "ℹ️ Info";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var bot = MyDiscordBot.Bot.BotInstance;
            var commands = bot.GetAllLegacyCommands();
            var prefix = Environment.GetEnvironmentVariable("PREFIX") ?? "!";

            // --- Detailed help: !help <command>
            if (args.Length > 0)
            {
                var target = commands.FirstOrDefault(c =>
                    string.Equals(c.Name, args[0], StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    await message.Channel.SendMessageAsync($"❓ I can’t find a command named `{args[0]}`.");
                    return;
                }

                var eb = new EmbedBuilder()
                    .WithTitle($"{prefix}{target.Name}")
                    .WithDescription(target.Description)
                    .AddField("Category", string.IsNullOrWhiteSpace(target.Category) ? "Other" : target.Category, true)
                    .AddField("Usage", $"{prefix}{target.Name} …", true)
                    .WithColor(new Color(0x5865F2));

                await message.Channel.SendMessageAsync(embed: eb.Build());
                return;
            }

            // --- Overview: group by category and render across two columns
            var byCat = commands
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Category) ? "Other" : c.Category)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ebList = new EmbedBuilder()
                .WithTitle("Help")
                .WithDescription($"Use `{prefix}help <command>` to see details.")
                .WithColor(new Color(0x5865F2));

            foreach (var g in byCat)
            {
                var list = string.Join("  ",
                    g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(c => $"`{prefix}{c.Name}`"));

                // Inline fields → Discord typically renders 2 per row on mobile, up to 3 on desktop.
                ebList.AddField(g.Key, list, inline: true);
            }

            // Pad to an even number of inline fields so rows tend to align as two visible columns.
            if (ebList.Fields.Count % 2 == 1)
                ebList.AddField("\u200B", "\u200B", inline: true);

            await message.Channel.SendMessageAsync(embed: ebList.Build());
        }
    }
}