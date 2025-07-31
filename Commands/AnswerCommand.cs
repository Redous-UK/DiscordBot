using Discord.WebSocket;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class AnswerCommand : ILegacyCommand
    {
        public string Name => "answer";
        public string Description => "Command to Answer the Trivia Question!";

        private static readonly ConcurrentDictionary<ulong, string> activeQuestions = new();
        private static readonly ConcurrentDictionary<ulong, int> userScores = new();

        public static void SetQuestion(ulong userId, string answer)
        {
            activeQuestions[userId] = answer;
        }

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (args.Length == 0)
            {
                await message.Channel.SendMessageAsync("Please provide an answer. Usage: `!answer your_answer_here`");
                return;
            }

            string userAnswer = string.Join(" ", args).ToLower();
            ulong userId = message.Author.Id;

            if (!activeQuestions.TryGetValue(userId, out string correctAnswer))
            {
                await message.Channel.SendMessageAsync("You don't have an active trivia question. Use `!trivia` first.");
                return;
            }

            if (userAnswer == correctAnswer)
            {
                userScores.AddOrUpdate(userId, 1, (_, score) => score + 1);
                await message.Channel.SendMessageAsync($"✅ Correct! Your score is now {userScores[userId]}.");
            }
            else
            {
                await message.Channel.SendMessageAsync($"❌ Nope! The correct answer was **{correctAnswer}**. Your score remains {(userScores.TryGetValue(userId, out int score) ? score : 0)}.");
            }

            activeQuestions.TryRemove(userId, out _);
        }

    }
}
