using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class UserInfoCommand : ILegacyCommand
    {
        public string Name => "userinfo";
        public string Description => "Shows detailed info about a user or yourself.";
        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command can only be used in a server.");
                return;
            }

            SocketGuildUser user = message.MentionedUsers.FirstOrDefault() as SocketGuildUser
                                   ?? message.Author as SocketGuildUser;

            if (user == null)
            {
                await message.Channel.SendMessageAsync("❌ Could not resolve user.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"{user.Username}'s Info")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .AddField("Username", $"{user.Username}#{user.Discriminator}", true)
                .AddField("Nickname", user.Nickname ?? "None", true)
                .AddField("ID", user.Id, true)
                .AddField("Is Bot", user.IsBot ? "✅ Yes" : "❌ No", true)
                .AddField("Status", user.Status.ToString(), true)
                .AddField("Activity", user.Activities.FirstOrDefault()?.Name ?? "None", true)
                .AddField("Joined Server", user.JoinedAt?.ToString("f") ?? "Unknown", true)
                .AddField("Account Created", user.CreatedAt.ToString("f"), true)
                .AddField("Roles", string.Join(", ", user.Roles.Where(r => !r.IsEveryone).Select(r => r.Name)) is string roles && roles != "" ? roles : "None")
                .WithColor(Color.Gold)
                .WithFooter($"Requested by {message.Author.Username}");

            if (user.PremiumSince.HasValue)
            {
                embed.AddField("Boosting Since", user.PremiumSince.Value.ToString("f"), true);
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}