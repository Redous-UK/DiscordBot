using Discord.WebSocket;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class HelpCommand : ILegacyCommand
    {
        public string Name => "help";
        public string Description => "Lists all available commands.";

        public Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("?? **Available Commands:**");

            foreach (var cmd in Program.BotInstance.GetAllLegacyCommands().OrderBy(c => c.Name))
            {
                sb.AppendLine($"- `{Program.BotInstance.Prefix}{cmd.Name}` \t {cmd.Description}");
            }

            return message.Channel.SendMessageAsync(sb.ToString());
        }
    }
}
