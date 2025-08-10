using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class RpsCommand : ILegacyCommand
    {
        public string Name => "rps";

        public string Description => "Command to play Rock, Paper, Scissors with the bot";

        public string Category => "🎮 Fun & Games";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var choices = new[] { "rock", "paper", "scissors" };
            var rand = new Random();
            string botChoice = choices[rand.Next(choices.Length)];

            string userChoice = args.Length > 0 ? args[0].ToLower() : null!;

            if (!choices.Contains(userChoice))
            {
                await message.Channel.SendMessageAsync("Please choose rock, paper, or scissors. Usage: `!rps rock`");
                return;
            }

            string result;
            if (userChoice == botChoice)
                result = "It's a draw!";
            else if ((userChoice == "rock" && botChoice == "scissors") ||
                     (userChoice == "paper" && botChoice == "rock") ||
                     (userChoice == "scissors" && botChoice == "paper"))
                result = "You win!";
            else
                result = "I win!";

            await message.Channel.SendMessageAsync($"You chose **{userChoice}**, I chose **{botChoice}**. {result}");
        }

    }
}
