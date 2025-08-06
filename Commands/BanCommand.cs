using Discord.WebSocket;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class BanCommand : ILegacyCommand
    {
        public string Name => "ban";
        public string Description => "Command to Ban People (Not Actually Coded Yet)!";
        public string Category => "🛠️ Moderation";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            await message.Channel.SendMessageAsync("🚫 Sorry, I can't actually ban users (yet). But imagine I did.");
        }
    }
}
