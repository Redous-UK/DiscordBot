using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RestartCommand : ILegacyCommand
    {
        public string Name => "restart";

        public string Description => "This command restarts the bot but can only be run by Admin.";
        public string Category => "⚙️ Settings & Config";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var user = message.Author as SocketGuildUser;
            if (user == null || !user.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("⛔ You must be an admin to restart the bot.");
                return;
            }

            await message.Channel.SendMessageAsync("🔁 Restarting bot...");

            // Delay to let the message go through
            await Task.Delay(1000);

            Environment.Exit(100); // Use exit code 100 for restart
        }
    }
}