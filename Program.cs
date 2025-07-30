using System.Threading.Tasks;

namespace MyDiscordBot
{
    public static class Program
    {
        public static Bot BotInstance { get; private set; }

        public static async Task Main(string[] args)
        {
            BotInstance = new Bot();
            await BotInstance.RunAsync();
        }
    }
}
