using Discord;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class HelpCommand : ILegacyCommand
    {
        public string Name => "help";
        public string Description => "Displays a list of available commands grouped by category.";
        public string Category => "🔧 Utility";

        public Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var commands = Bot.BotInstance.GetAllLegacyCommands();

            var grouped = commands
                .GroupBy(cmd => cmd.Category)
                .OrderBy(g => g.Key);

            var sb = new StringBuilder();

            foreach (var group in grouped)
            {
                sb.AppendLine($"**{group.Key}**");
                foreach (var cmd in group.OrderBy(c => c.Name))
                {
                    sb.AppendLine($"- `{cmd.Name}` — {cmd.Description}");
                }
                sb.AppendLine();
            }

            return message.Channel.SendMessageAsync(sb.ToString());
        }
    }
}