using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands.Deprecated
{
    [Obsolete("This command has been deprecated.")]
    public class LogStatusCommand : ILegacyCommand
    {
        public string Name => "logstatus";

        public string Description => "Command to list the debug modes as a list";

        public Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
                return message.Channel.SendMessageAsync("❌ This command must be used in a server.");

            ulong guildId = guildChannel.Guild.Id;
            var sb = new StringBuilder();

            sb.AppendLine("🛠️ **Logging Status for This Server** 🛠️");
            sb.AppendLine();

            sb.AppendLine($"**Debug Mode**: {(Bot.GetDebugMode(guildId) ? "✅ ENABLED" : "❌ DISABLED")}");
            sb.AppendLine("**Log Categories:**");

            foreach (var category in Enum.GetValues(typeof(LogCategory)).Cast<LogCategory>())
            {
                bool enabled = Bot.IsLogCategoryEnabled(guildId, category);
                sb.AppendLine($"- {category}: {(enabled ? "✅" : "❌")}");
            }

            return message.Channel.SendMessageAsync(sb.ToString());
        }
    }
}