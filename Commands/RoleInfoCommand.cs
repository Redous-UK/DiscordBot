using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RoleInfoCommand : ILegacyCommand
    {
        public string Name => "roleinfo";
        public string Description => "Displays information about a specific role.";
        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var guild = guildChannel.Guild;

            if (args.Length == 0)
            {
                await message.Channel.SendMessageAsync("❌ Please provide a role name.");
                return;
            }

            string search = string.Join(" ", args).ToLower();
            var role = guild.Roles.FirstOrDefault(r => r.Name.ToLower() == search);

            if (role == null)
            {
                await message.Channel.SendMessageAsync("❌ Role not found.");
                return;
            }

            int memberCount = guild.Users.Count(u => u.Roles.Contains(role));
            var permissions = role.Permissions.ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"🔎 Role Info: {role.Name}")
                .WithColor(role.Color == Color.Default ? Color.LightGrey : role.Color)
                .AddField("🆔 Role ID", role.Id, true)
                .AddField("👥 Members", memberCount, true)
                .AddField("🎨 Color", role.Color.ToString(), true)
                .AddField("🏷️ Mentionable", role.IsMentionable ? "Yes" : "No", true)
                .AddField("📌 Hoisted", role.IsHoisted ? "Yes" : "No", true)
                .AddField("⚙️ Managed", role.IsManaged ? "Yes" : "No", true)
                .AddField("🔐 Permissions", permissions.Any()
                    ? string.Join(", ", permissions.Take(10)) + (permissions.Count > 10 ? ", ..." : "")
                    : "None")
                .WithFooter($"Requested by {message.Author.Username}");

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}