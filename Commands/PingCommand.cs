using Discord.WebSocket;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class PingCommand : ILegacyCommand
    {
        public string Name => "ping";

        public string Description => "Command to ping the server - response should be Pong if the bot is working";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            await message.Channel.SendMessageAsync("Pong!");
        }

    }
}
