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
        public string Description => "Clears the channel";
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

            if (args.Length == 0)
            {
                await message.Channel.SendMessageAsync("❌ Usage: `!clear [x|all|user @user|bots|images]`");
                return;
            }

            var option = args[0].ToLower();

            if (option == "all")
            {
                _ = Task.Run(async () =>
                {
                    int deleted = 0;

                    try
                    {
                        while (true)
                        {
                            var messages = await textChannel.GetMessagesAsync(100).FlattenAsync();
                            var deletable = messages
                                .Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays <= 14)
                                .ToList();

                            if (!deletable.Any()) break;

                            await textChannel.DeleteMessagesAsync(deletable);
                            deleted += deletable.Count;

                            await Task.Delay(1000); // avoid rate limit
                        }

                        await textChannel.SendMessageAsync($"🧹 Cleared all messages in this channel ({deleted} total).");
                    }
                    catch (Exception ex)
                    {
                        await textChannel.SendMessageAsync($"❌ Error while clearing: {ex.Message}");
                    }
                });

                await message.Channel.SendMessageAsync("⏳ Clearing all recent messages...");
                return;
            }

            // Fetch up to 100 messages for other options
            var messages = await textChannel.GetMessagesAsync(100).FlattenAsync();

            switch (option)
            {
                case "bots":
                    var botMessages = messages.Where(m => m.Author.IsBot && (DateTimeOffset.UtcNow - m.Timestamp).TotalDays <= 14).ToList();
                    await textChannel.DeleteMessagesAsync(botMessages);
                    await textChannel.SendMessageAsync($"🤖 Cleared {botMessages.Count} bot messages.");
                    break;

                case "images":
                    var imageMessages = messages.Where(m =>
                        m.Attachments.Any() && (DateTimeOffset.UtcNow - m.Timestamp).TotalDays <= 14).ToList();

                    await textChannel.DeleteMessagesAsync(imageMessages);
                    await textChannel.SendMessageAsync($"🖼️ Cleared {imageMessages.Count} messages with images/attachments.");
                    break;

                case "user":
                    if (args.Length < 2 || message.MentionedUsers.Count == 0)
                    {
                        await message.Channel.SendMessageAsync("❌ Please mention a user. Example: `!clear user @User`");
                        return;
                    }

                    var targetUser = message.MentionedUsers.First();
                    var userMessages = messages
                        .Where(m => m.Author.Id == targetUser.Id && (DateTimeOffset.UtcNow - m.Timestamp).TotalDays <= 14)
                        .ToList();

                    await textChannel.DeleteMessagesAsync(userMessages);
                    await textChannel.SendMessageAsync($"👤 Cleared {userMessages.Count} messages from {targetUser.Mention}.");
                    break;

                default:
                    if (int.TryParse(option, out int count))
                    {
                        if (count < 1 || count > 100)
                        {
                            await message.Channel.SendMessageAsync("❌ Please specify a number between 1 and 100.");
                            return;
                        }

                        var toDelete = messages
                            .Take(count + 1)
                            .Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays <= 14)
                            .ToList();

                        await textChannel.DeleteMessagesAsync(toDelete);
                        await textChannel.SendMessageAsync($"🧹 Cleared {toDelete.Count - 1} messages.");
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync("❌ Unknown option. Usage: `!clear [x|all|user @user|bots|images]`");
                    }
                    break;
            }
        }
    }
}