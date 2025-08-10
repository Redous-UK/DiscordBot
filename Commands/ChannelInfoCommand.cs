using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class ChannelInfoCommand : ILegacyCommand
    {
        public string Name => "channelinfo";
        public string Description => "Displays information about a specific text or voice channel.";
        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var guild = guildChannel.Guild;
            SocketGuildChannel targetChannel = null!;

            if (args.Length == 0)
            {
                targetChannel = guildChannel;
            }
            else
            {
                string search = string.Join(" ", args).ToLower();
                targetChannel = guild.Channels.FirstOrDefault(c => c.Name.ToLower(System.Globalization.CultureInfo.CurrentCulture) == search) ?? null!;
            }

            if (targetChannel == null)
            {
                await message.Channel.SendMessageAsync("❌ Channel not found.");
                return;
            }

            string categoryName = "None";
            if (targetChannel is SocketTextChannel txt && txt.Category != null)
                categoryName = txt.Category.Name;
            else if (targetChannel is SocketVoiceChannel vc && vc.Category != null)
                categoryName = vc.Category.Name;

            var embed = new EmbedBuilder()
                .WithTitle($"📺 Channel Info: #{targetChannel.Name}")
                .WithColor(Color.Orange)
                .AddField("🆔 Channel ID", targetChannel.Id, true)
                .AddField("📂 Category", categoryName, true)
                .AddField("📦 Type", targetChannel.GetType().Name.Replace("Socket", "").Replace("Channel", ""), true)
                .AddField("📌 Position", targetChannel.Position, true)
                .AddField("🔐 Overwrites", targetChannel.PermissionOverwrites.Count, true)
                .AddField("📅 Created On", targetChannel.CreatedAt.ToString("f"), true)
                .WithFooter($"Requested by {message.Author.Username}");

            if (targetChannel is SocketTextChannel text)
            {
                embed.AddField("💬 Topic", string.IsNullOrWhiteSpace(text.Topic) ? "None" : text.Topic);
                embed.AddField("🔞 NSFW", text.IsNsfw ? "Yes" : "No", true);
            }
            else if (targetChannel is SocketVoiceChannel voice)
            {
                embed.AddField("🎙 Bitrate", voice.Bitrate + " bps", true);
                embed.AddField("👥 User Limit", voice.UserLimit > 0 ? voice.UserLimit : "Unlimited", true);
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}