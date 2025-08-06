using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class GuildStatsCommand : ILegacyCommand
    {
        public string Name => "guildstats";
        public string Description => "Displays statistics and details about the current server.";

        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var guild = guildChannel.Guild;
            var members = await guild.GetUsersAsync().FlattenAsync();

            var totalMembers = members.Count();
            var botCount = members.Count(m => m.IsBot);
            var userCount = totalMembers - botCount;

            var online = members.Count(m => m.Status == UserStatus.Online);
            var idle = members.Count(m => m.Status == UserStatus.Idle);
            var dnd = members.Count(m => m.Status == UserStatus.DoNotDisturb);
            var offline = members.Count(m => m.Status == UserStatus.Offline);

            var textChannels = guild.TextChannels.Count;
            var voiceChannels = guild.VoiceChannels.Count;
            var categories = guild.CategoryChannels.Count;
            var roles = guild.Roles.Count;
            var emojis = guild.Emotes.Count;
            var creationDate = guild.CreatedAt.ToString("f");

            var embed = new EmbedBuilder()
                .WithTitle($"📊 Stats for {guild.Name}")
                .WithThumbnailUrl(guild.IconUrl)
                .WithColor(Color.Blue)
                .AddField("👥 Members", $"Total: {totalMembers}\nUsers: {userCount}\nBots: {botCount}", true)
                .AddField("🟢 Status", $"Online: {online}\nIdle: {idle}\nDND: {dnd}\nOffline: {offline}", true)
                .AddField("#️⃣ Channels", $"Text: {textChannels}\nVoice: {voiceChannels}\nCategories: {categories}", true)
                .AddField("🔧 Other Stats", $"Roles: {roles}\nEmojis: {emojis}", true)
                .AddField("📅 Created On", creationDate, true)
                .WithFooter($"Server ID: {guild.Id} • Requested by {message.Author.Username}");

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}