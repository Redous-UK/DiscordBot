using Discord.WebSocket;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RemindersCommand : ILegacyCommand
    {
        public string Name => "reminders";

        public string Description => "Lists all your reminders.";
        public string Category => "🔧 Utility";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("❌ This command must be used in a server.");
                return;
            }

            var service = new Services.ReminderService(Program.BotInstance.GetClient());
            var list = service.GetReminders(message.Author.Id);

            if (list.Count == 0)
            {
                await message.Channel.SendMessageAsync("📭 You have no reminders.");
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"📋 **Reminders for {message.Author.Username}:**");

                foreach (var item in list)
                    sb.AppendLine($"- {item}");

                await message.Channel.SendMessageAsync(sb.ToString());
            }
        }
    }
}