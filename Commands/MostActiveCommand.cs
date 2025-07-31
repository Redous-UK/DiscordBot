using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class MostActiveCommand : ILegacyCommand
    {
        public string Name => "mostactive";
        public string Description => "Displays the most active user by message count.";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            ulong guildId = guildChannel.Guild.Id;
            string path = Path.Combine(AppContext.BaseDirectory, "message-counts.json");

            if (!File.Exists(path))
            {
                await message.Channel.SendMessageAsync("📭 No message activity has been tracked yet.");
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            var guildData = data
                .Where(kvp => kvp.Key.StartsWith(guildId.ToString()))
                .ToDictionary(kvp => kvp.Key.Split('-')[1], kvp => kvp.Value);

            if (guildData.Count == 0)
            {
                await message.Channel.SendMessageAsync("📭 No message activity recorded for this server.");
                return;
            }

            var top = guildData.OrderByDescending(kvp => kvp.Value).First();
            ulong userId = ulong.Parse(top.Key);
            int count = top.Value;
            var user = guildChannel.Guild.GetUser(userId);

            string name = user != null ? user.Mention : $"<@{userId}>";
            await message.Channel.SendMessageAsync($"👑 Most active user: {name} with **{count:N0}** messages.");
        }
    }
}