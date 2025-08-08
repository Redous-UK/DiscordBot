using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class HelpCommand : ILegacyCommand
    {
        public string Name => "help";
        public string Description => "Shows a list of commands available";
        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var commands = Bot.BotInstance.GetAllLegacyCommands();

            var grouped = commands
                .GroupBy(c =>
                {
                    var categoryProp = c.GetType().GetProperty("Category");
                    return categoryProp?.GetValue(c) as string ?? "🔧 Uncategorized";
                })
                .OrderBy(g => g.Key);

            var sb = new StringBuilder();
            sb.AppendLine("📖 **Bot Commands Help**");
            sb.AppendLine();

            foreach (var group in grouped)
            {
                sb.AppendLine($"**{group.Key}**");
                foreach (var cmd in group)
                {
                    sb.AppendLine($"\t`!{cmd.Name}`");
                }
                sb.AppendLine();
            }

            await message.Channel.SendMessageAsync(sb.ToString());
        }
    }
}