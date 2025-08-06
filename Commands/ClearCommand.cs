using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class ClearCommand : ILegacyCommand
    {
        public string Name => "clear";
        public string Description => "Clears a number of messages from the current channel. Admin only.";
        public string Category => "🛠️ Moderation";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketTextChannel textChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a text channel.");
                return;
            }

            if (message.Author is not SocketGuildUser user || !user.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("❌ You must be an administrator to use this command.");
                return;
            }

            int amount = 1;
            if (args.Length > 0 && (!int.TryParse(args[0], out amount) || amount < 1 || amount > 100))
            {
                await message.Channel.SendMessageAsync("❌ Please enter a valid number between 1 and 100.");
                return;
            }

            var messages = await textChannel.GetMessagesAsync(limit: amount + 1).FlattenAsync(); // +1 to include the !clear command
            var filtered = messages.Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14).ToList();

            await textChannel.DeleteMessagesAsync(filtered);

            var confirm = await textChannel.SendMessageAsync($"✅ Deleted {filtered.Count - 1} messages."); // -1 for the command itself
            await Task.Delay(3000);
            await confirm.DeleteAsync();

            // Logging
            Bot.BotInstance?.LogMessage(textChannel.Guild.Id, $"{user.Username} cleared {filtered.Count - 1} messages in #{textChannel.Name}.", LogCategory.Moderation);
        }
    }
}