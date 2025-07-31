using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands.Deprecated
{
    [Obsolete("This command has been deprecated.")]
    public class SettingsCommand : ILegacyCommand
    {
        public string Name => "settings";
        public string Description => "Shows current server settings.";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("This command must be used in a server.");
                return;
            }

            var guildId = guildChannel.Guild.Id;
            var settings = Bot.GetSettings(guildId);
            var sb = new StringBuilder();

            sb.AppendLine($"📋 **Settings for {guildChannel.Guild.Name}**:");
            sb.AppendLine();
            sb.AppendLine($"- **Nickname**: {settings.Nickname ?? "Not set"}");
            sb.AppendLine($"- **Debug Mode**: {(settings.DebugEnabled ? "✅ Enabled" : "❌ Disabled")}");
            sb.AppendLine($"- **Log Categories**: {(settings.LogCategories?.Any() == true ? string.Join(", ", settings.LogCategories) : "None")}");
            sb.AppendLine($"- **Birthday Channel**: {(settings.BirthdayChannelId > 0 ? $"<#{settings.BirthdayChannelId}>" : "Not set")}");

            await message.Channel.SendMessageAsync(sb.ToString());
        }
    }
}