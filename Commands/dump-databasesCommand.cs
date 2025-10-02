using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace MyDiscordBot.Commands
{
    public class DumpDatabasesCommand : ILegacyCommand
    {
        public string Name => "dump-databases";
        public string Description => "Admin only. Dumps the JSON databases used by the bot.";
        public string Category => "ℹ️ Info";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var userMessage = message as SocketUserMessage;
            var context = message.Channel as SocketTextChannel;

            if (context == null)
            {
                await message.Channel.SendMessageAsync("This command must be run in a server.");
                return;
            }

            // Only admins can use this
            var user = context.GetUser(message.Author.Id);
            if (!(user.GuildPermissions.Administrator))
            {
                await message.Channel.SendMessageAsync("🚫 You need Administrator permissions to run this command.");
                return;
            }

            string[] dbFiles = { "reminders.json", "birthdays.json" };
            foreach (var dbFile in dbFiles)
            {
                if (File.Exists(dbFile))
                {
                    try
                    {
                        await message.Channel.SendFileAsync(dbFile, $"📂 Dump of `{dbFile}`:");
                    }
                    catch (Exception ex)
                    {
                        await message.Channel.SendMessageAsync($"⚠️ Could not send `{dbFile}`: {ex.Message}");
                    }
                }
                else
                {
                    await message.Channel.SendMessageAsync($"ℹ️ `{dbFile}` not found.");
                }
            }
        }
    }
}