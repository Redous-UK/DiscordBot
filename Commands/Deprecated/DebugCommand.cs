using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    [Obsolete("This command has been deprecated.")]
    public class DebugCommand : ILegacyCommand
    {
        public string Name => "debug";

        public string Description => "Command to set debugging options in the server!";

        public Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
                return message.Channel.SendMessageAsync("❌ This command must be used in a server.");

            ulong guildId = guildChannel.Guild.Id;

            if (args.Length < 1 || (args[0].ToLower() != "on" && args[0].ToLower() != "off"))
            {
                string status = Bot.GetDebugMode(guildId) ? "ENABLED" : "DISABLED";
                return message.Channel.SendMessageAsync($"🛠️ Debug mode is currently **{status}**.");
            }

            bool enable = args[0].ToLower() == "on";
            Bot.SetDebugMode(guildId, enable);

            string result = enable ? "ENABLED" : "DISABLED";
            return message.Channel.SendMessageAsync($"🛠️ Debug mode has been **{result}** for this server.");
        }
    }
}
