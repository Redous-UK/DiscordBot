using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class TriviaCommand : ILegacyCommand
    {
        public string Name => "trivia";
        public string Description => "Command to get the Trivia Question!";
        public string Category => "🎮 Fun & Games";

        private static readonly List<(string question, string answer)> Questions = new()
        {
            ("What is the capital of France?", "paris"),
            ("What is 5 + 7?", "12"),
            ("Which planet is known as the Red Planet?", "mars"),
            ("What language is primarily spoken in Brazil?", "portuguese"),
            ("Who wrote 'Romeo and Juliet'?", "shakespeare")
        };

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            var rand = new Random();
            var (question, answer) = Questions[rand.Next(Questions.Count)];

            AnswerCommand.SetQuestion(message.Author.Id, answer);

            await message.Channel.SendMessageAsync($"{question} (Reply with `!answer your_answer`)");
        }

    }
}
