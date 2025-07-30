using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class KickCommand : ILegacyCommand
    {
        public string Name => "kick";

        public string Description => "Command to Kick User (Not coded yet) !";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            await message.Channel.SendMessageAsync("👢 Consider the user virtually kicked. Just kidding!");
        }

    }
}
