using Discord;
using Discord.API.AuditLogs;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class ModLogCommand : ILegacyCommand
    {
        public string Name => "modlog";
        public string Description => "Displays recent moderation actions from the audit log.";

        public string Category => "🛠️ Moderation";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var user = message.Author as SocketGuildUser;
            if (user == null || !(user.GuildPermissions.Administrator || user.GuildPermissions.ManageGuild))
            {
                await message.Channel.SendMessageAsync("🚫 You do not have permission to run this command.");
                return;
            }

            var guild = guildChannel.Guild;
            var logs = await guild.GetAuditLogsAsync(10).FlattenAsync();

            var sb = new StringBuilder();
            sb.AppendLine("🛡️ **Recent Moderation Actions**");
            sb.AppendLine();

            foreach (var entry in logs)
            {
                string moderator = entry.User?.Username ?? "Unknown";
                string action = entry.Action.ToString();
                string target = "N/A";

                switch (entry.Action)
                {
                    case ActionType.Ban:
                        if (entry.Data is BanAuditLogData banData)
                            target = banData.Target?.Username ?? "Unknown";
                        break;

                    case ActionType.Unban:
                        if (entry.Data is UnbanAuditLogData unbanData)
                            target = unbanData.Target?.Username ?? "Unknown";
                        break;

                    case ActionType.Kick:
                        if (entry.Data is KickAuditLogData kickData)
                            target = kickData.Target?.Username ?? "Unknown";
                        break;

                    default:
                        continue; // Skip unrelated logs
                }

                sb.AppendLine($"- `{action}` → **{target}** by **{moderator}** at `{entry.CreatedAt:f}`");
            }

            if (sb.Length < 50)
            {
                await message.Channel.SendMessageAsync("⚠️ No recent moderation actions found.");
                return;
            }

            await message.Channel.SendMessageAsync(sb.ToString());
        }
    }
}