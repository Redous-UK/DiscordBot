using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    [Obsolete("This command has been deprecated.")]
    public class LogCommand : ILegacyCommand
    {
        public string Name => "log";

        public string Description => "Command to set Log enabled : disabled!";

        public Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
                return message.Channel.SendMessageAsync("❌ This command must be used in a server.");

            ulong guildId = guildChannel.Guild.Id;

            if (args.Length < 2 || args[0].ToLower() != "toggle")
                return message.Channel.SendMessageAsync("Usage: `!log toggle [category]`\nExample: `!log toggle birthdaycheck`");

            string categoryInput = args[1];
            if (!Enum.TryParse<LogCategory>(categoryInput, ignoreCase: true, out var category))
                return message.Channel.SendMessageAsync($"❌ Unknown log category: `{categoryInput}`");

            bool enabled = Bot.ToggleLogCategory(guildId, category);
            string status = enabled ? "ENABLED ✅" : "DISABLED ❌";
            return message.Channel.SendMessageAsync($"[{category}] logging is now {status} for this server.");
        }
    }
}