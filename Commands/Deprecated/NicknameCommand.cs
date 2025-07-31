using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands.Deprecated
{
    [Obsolete("This command has been deprecated.")]
    public class NicknameCommand : ILegacyCommand
    {
        public string Name => "nickname";
        public string Description => "Sets or clears the bot's nickname in this server.";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var guild = guildChannel.Guild;
            var settings = Bot.GetSettings(guild.Id);
            var user = (SocketGuildUser)message.Author;

            if (!user.GuildPermissions.Administrator)
            {
                await message.Channel.SendMessageAsync("❌ You need to be an administrator to change the bot's nickname.");
                return;
            }

            var botUser = guild.GetUser(Bot.BotInstance.GetClient().CurrentUser.Id);

            if (args.Length == 0 || args[0].ToLower() == "clear")
            {
                settings.Nickname = null;
                try
                {
                    await botUser.ModifyAsync(p => p.Nickname = null);
                    await message.Channel.SendMessageAsync("🔄 Nickname has been reset to the default bot name.");
                }
                catch
                {
                    await message.Channel.SendMessageAsync("⚠️ I don't have permission to change my nickname in this server.");
                }
            }
            else
            {
                string newNick = string.Join(" ", args);
                settings.Nickname = newNick;
                try
                {
                    await botUser.ModifyAsync(p => p.Nickname = newNick);
                    await message.Channel.SendMessageAsync($"✅ Nickname changed to: **{newNick}**");
                }
                catch
                {
                    await message.Channel.SendMessageAsync("⚠️ I don't have permission to change my nickname in this server.");
                }
            }

            Bot.BotInstance.SaveGuildSettings();
        }
    }
}