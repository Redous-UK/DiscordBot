using Discord.WebSocket;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class EchoCommand : ILegacyCommand
    {
        public string Name => "echo";

        public string Description => "Command to respond with whatever is requested!";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            string response = args.Length > 0 ? string.Join(" ", args) : "You didn't say anything to echo.";
            await message.Channel.SendMessageAsync(response);
        }

    }
}
