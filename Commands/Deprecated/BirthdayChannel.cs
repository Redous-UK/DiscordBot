using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands.Deprecated
{
    [Obsolete("This command has been deprecated.")]
    public class BirthdayChannelCommand : ILegacyCommand
    {
        public string Name => "birthdaychannel";
        public string Description => "Command to set the birthday channel!";
        private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "birthday-channels.json");

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("This command must be used in a server.");
                return;
            }

            var guild = guildChannel.Guild;
            var guildId = guild.Id;
            var data = LoadChannelConfig();

            if (args.Length > 0 && args[0].ToLower() == "set")
            {
                var user = message.Author as SocketGuildUser;
                if (user == null || !user.GuildPermissions.Administrator)
                {
                    await message.Channel.SendMessageAsync("❌ Only server admins can set the birthday channel.");
                    return;
                }

                if (message.MentionedChannels.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Please mention a text channel. Example: `!birthdaychannel set #general`");
                    return;
                }

                var targetChannel = message.MentionedChannels.First() as SocketTextChannel;
                if (targetChannel == null)
                {
                    await message.Channel.SendMessageAsync("❌ Only text channels can be used for birthday messages.");
                    return;
                }

                data[guildId] = targetChannel.Id;
                SaveChannelConfig(data);

                await message.Channel.SendMessageAsync($"✅ Birthday messages will now be sent in {targetChannel.Mention}");
            }
            else if (args.Length > 0 && args[0].ToLower() == "get")
            {
                if (data.TryGetValue(guildId, out var channelId))
                {
                    var channel = guild.GetTextChannel(channelId);
                    if (channel != null)
                    {
                        await message.Channel.SendMessageAsync($"🎂 Birthday messages are currently sent in {channel.Mention}");
                    }
                    else
                    {
                        await message.Channel.SendMessageAsync("⚠️ The configured channel could not be found. You may need to re-set it.");
                    }
                }
                else
                {
                    await message.Channel.SendMessageAsync("❌ No birthday channel is set for this server.");
                }
            }
            else if (args.Length > 0 && args[0].ToLower() == "clear")
            {
                var user = message.Author as SocketGuildUser;
                if (user == null || !user.GuildPermissions.Administrator)
                {
                    await message.Channel.SendMessageAsync("❌ Only server admins can clear the birthday channel.");
                    return;
                }

                if (data.Remove(guildId))
                {
                    SaveChannelConfig(data);
                    await message.Channel.SendMessageAsync("🧹 Birthday channel has been cleared for this server.");
                }
                else
                {
                    await message.Channel.SendMessageAsync("ℹ️ No birthday channel was set for this server.");
                }
            }
            else
            {
                await message.Channel.SendMessageAsync("**Usage:**\n" +
                    "`!birthdaychannel set #channel` – Set the birthday message channel\n" +
                    "`!birthdaychannel get` – Show the currently configured channel\n" +
                    "`!birthdaychannel clear` – Clear the configured birthday channel");
            }
        }

        private Dictionary<ulong, ulong> LoadChannelConfig()
        {
            if (!File.Exists(Path)) return new();
            return JsonSerializer.Deserialize<Dictionary<ulong, ulong>>(File.ReadAllText(Path)) ?? new();
        }

        private void SaveChannelConfig(Dictionary<ulong, ulong> data)
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}