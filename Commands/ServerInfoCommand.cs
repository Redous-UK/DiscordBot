using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class ServerInfoCommand : ILegacyCommand
    {
        public string Name => "serverinfo";
        public string Description => "Displays information about the current Discord server.";

        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var guild = guildChannel.Guild;

            int totalMembers = guild.MemberCount;
            int botCount = guild.Users.Count(u => u.IsBot);
            int humanCount = totalMembers - botCount;
            int onlineCount = guild.Users.Count(u => u.Status != UserStatus.Offline);
            int textChannels = guild.TextChannels.Count;
            int voiceChannels = guild.VoiceChannels.Count;

            var embed = new EmbedBuilder()
                .WithTitle($"📊 Server Info: {guild.Name}")
                .WithThumbnailUrl(guild.IconUrl)
                .WithColor(Color.Purple)
                .AddField("🆔 Server ID", guild.Id, true)
                .AddField("👑 Owner", guild.Owner?.Username ?? "Unknown", true)
                .AddField("🗓️ Created On", guild.CreatedAt.ToString("f"), true)
                .AddField("🌐 Region", guild.VoiceRegionId ?? "Unknown", true)
                .AddField("👥 Members", $"Total: {totalMembers}\nHumans: {humanCount}\nBots: {botCount}", true)
                .AddField("💡 Online Now", onlineCount, true)
                .AddField("#️⃣ Text Channels", textChannels, true)
                .AddField("🔊 Voice Channels", voiceChannels, true)
                .AddField("📛 Roles", guild.Roles.Count, true)
                .AddField("🚀 Boosts", $"{guild.PremiumSubscriptionCount} (Tier {guild.PremiumTier})", true)
                .WithFooter($"Requested by {message.Author.Username}");

            if (!string.IsNullOrEmpty(guild.BannerUrl))
            {
                embed.WithImageUrl(guild.BannerUrl);
            }
            else if (!string.IsNullOrEmpty(guild.SplashUrl))
            {
                embed.WithImageUrl(guild.SplashUrl);
            }

            // Top 10 roles by position (excluding @everyone)
            var topRoles = guild.Roles
                .Where(r => !r.IsEveryone)
                .OrderByDescending(r => r.Position)
                .Take(10)
                .Select(r => r.Name);

            if (topRoles.Any())
            {
                embed.AddField("🔰 Top Roles", string.Join(", ", topRoles));
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}