using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class SettingsEditorCommand : ILegacyCommand
    {
        public string Name => "settings";
        public string Description => "Views or updates settings using subcommands.";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            ulong guildId = guildChannel.Guild.Id;
            var settings = Bot.GetSettings(guildId);

            if (args.Length == 0 || args[0].ToLower() == "view")
            {
                var sb = new StringBuilder();
                sb.AppendLine($"📋 **Settings for {guildChannel.Guild.Name}**:");
                sb.AppendLine($"- **Nickname**: {settings.Nickname ?? "Not set"}");
                sb.AppendLine($"- **Debug Mode**: {(settings.DebugEnabled ? "✅ Enabled" : "❌ Disabled")}");
                sb.AppendLine($"- **Log Categories**: {(settings.LogCategories?.Any() == true ? string.Join(", ", settings.LogCategories) : "None")}");
                sb.AppendLine($"- **Birthday Channel**: {(settings.BirthdayChannelId > 0 ? $"<#{settings.BirthdayChannelId}>" : "Not set")}\n");

                sb.AppendLine("_Example usage: !settings set debug true_");
                sb.AppendLine("_Settings: nickname, debug, log, birthdaychannel_");

                await message.Channel.SendMessageAsync(sb.ToString());
                return;
            }

            if (args.Length == 1 && args[0].ToLower() == "logcategories")
            {
                var categories = Enum.GetNames(typeof(LogCategory));
                var list = string.Join("\n- ", categories);
                await message.Channel.SendMessageAsync($"🗂️ **Available Log Categories:**\n- {list}");
                return;
            }

            if (args.Length < 2 || args[0].ToLower() != "set")
            {
                await message.Channel.SendMessageAsync("❌ Usage: `!settings set <key> <value>` or `!settings view`");
                return;
            }

            string key = args[1].ToLower();
            string value = string.Join(" ", args.Skip(2));

            switch (key)
            {
                case "nickname":
                    var user = (SocketGuildUser)message.Author;
                    if (!user.GuildPermissions.Administrator)
                    {
                        await message.Channel.SendMessageAsync("❌ You must be an administrator to change the bot's nickname.");
                        return;
                    }

                    var botUser = guildChannel.Guild.GetUser(Bot.BotInstance.GetClient().CurrentUser.Id);

                    if (string.IsNullOrWhiteSpace(value) || value.ToLower() == "clear")
                    {
                        settings.Nickname = null;
                        try { await botUser.ModifyAsync(p => p.Nickname = null); } catch { }
                        await message.Channel.SendMessageAsync("🔄 Nickname cleared.");
                    }
                    else
                    {
                        settings.Nickname = value;
                        try { await botUser.ModifyAsync(p => p.Nickname = value); } catch { }
                        await message.Channel.SendMessageAsync($"✅ Nickname set to `{value}`.");
                    }
                    break;

                case "debug":
                    if (bool.TryParse(value, out bool debug))
                    {
                        settings.DebugEnabled = debug;
                        await message.Channel.SendMessageAsync($"🔧 Debug mode set to {(debug ? "✅ Enabled" : "❌ Disabled")}.");
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync("⚠️ Please provide `true` or `false` for debug setting.");
                    }
                    break;

                case "log":
                    if (args.Length < 4)
                    {
                        await message.Channel.SendMessageAsync("❌ Usage: `!settings set log <CategoryName> on/off`");
                        return;
                    }

                    string logCategory = args[2];
                    string toggle = args[3].ToLower();

                    if (!Enum.TryParse<LogCategory>(logCategory, true, out var category))
                    {
                        await message.Channel.SendMessageAsync("⚠️ Invalid log category.");
                        return;
                    }

                    if (toggle != "on" && toggle != "off")
                    {
                        await message.Channel.SendMessageAsync("⚠️ Use `on` or `off` to enable/disable log category.");
                        return;
                    }

                    if (toggle == "on")
                        settings.LogCategories.Add(category.ToString());
                    else
                        settings.LogCategories.Remove(category.ToString());

                    await message.Channel.SendMessageAsync($"📝 Log category `{category}` {(toggle == "on" ? "enabled" : "disabled")}.");
                    break;

                case "birthdaychannel":
                    if (string.IsNullOrWhiteSpace(value) || value.ToLower() == "clear")
                    {
                        settings.BirthdayChannelId = 0;
                        await message.Channel.SendMessageAsync("🎂 Birthday channel cleared.");
                    }
                    else if (MentionUtils.TryParseChannel(value, out ulong channelId))
                    {
                        settings.BirthdayChannelId = channelId;
                        await message.Channel.SendMessageAsync($"🎉 Birthday messages will now be posted in <#{channelId}>.");
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync("⚠️ Please mention a valid text channel.");
                    }
                    break;

                default:
                    await message.Channel.SendMessageAsync("❌ Unknown setting. Use `nickname`, `debug`, `log`, or `birthdaychannel`.");
                    return;
            }

            Bot.BotInstance.SaveGuildSettings();
        }
    }
}