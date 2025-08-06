using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class EmojiInfoCommand : ILegacyCommand
    {
        public string Name => "emojiinfo";
        public string Description => "Shows detailed information about a custom emoji.";
        public string Category => "📊 Info & Stats";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            if (args.Length == 0)
            {
                await message.Channel.SendMessageAsync("❌ Please provide an emoji name or ID.");
                return;
            }

            var guild = guildChannel.Guild;
            string search = string.Join(" ", args).Trim();
            var emoji = guild.Emotes.FirstOrDefault(e =>
                e.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
                e.Id.ToString() == search);

            if (emoji == null)
            {
                await message.Channel.SendMessageAsync("❌ Custom emoji not found in this server.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"🧩 Emoji Info: {emoji.Name}")
                .WithThumbnailUrl(emoji.Url)
                .WithColor(Color.Gold)
                .AddField("🆔 Emoji ID", emoji.Id, true)
                .AddField("🎞️ Animated", emoji.Animated ? "Yes" : "No", true)
                .AddField("🔐 Roles Allowed", emoji.RoleIds.Count > 0
                    ? string.Join(", ", emoji.RoleIds.Select(id => guild.GetRole(id)?.Name ?? id.ToString()))
                    : "Everyone")
                .AddField("📅 Created", emoji.CreatedAt.ToString("f"), true)
                .AddField("🔗 Image", $"[Click here]({emoji.Url})");

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}