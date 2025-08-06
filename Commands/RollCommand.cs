using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RollCommand : ILegacyCommand
    {
        public string Name => "roll";

        public string Description => "Command to randomly roll a dice!";
        public string Category => "🎮 Fun & Games";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var rand = new Random();
            int result = rand.Next(1, 101); // Roll between 1 and 100
            await message.Channel.SendMessageAsync($":game_die: You rolled a **{result}**!");
        }

    }
}
