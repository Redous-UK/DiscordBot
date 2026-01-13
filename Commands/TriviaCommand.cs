using Discord;
using Discord.WebSocket;
using MyDiscordBot.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public sealed class TriviaCommand : ILegacyCommand
    {
        public string Name => "trivia";
        public string Description => "Per-guild trivia game using OpenTDB. !trivia start|next|answer|score|stop";
        public string Usage => "!trivia [start|next|answer|score|stop]";
        public string Category => "🎮 Fun & Games";

        // One game per guild
        private static readonly ConcurrentDictionary<ulong, TriviaGame> _games = new();

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                await message.Channel.SendMessageAsync("Trivia only works in servers (not DMs).");
                return;
            }

            var guildId = gc.Guild.Id;
            var game = _games.GetOrAdd(guildId, _ => new TriviaGame());

            var sub = (args.Length > 0 ? args[0] : "start").ToLowerInvariant();

            switch (sub)
            {
                case "start":
                    await StartAsync(message, guildId, game);
                    break;

                case "next":
                    await NextAsync(message, guildId, game);
                    break;

                case "answer":
                case "a":
                    await AnswerAsync(message, guildId, game, args.Skip(1).ToArray());
                    break;

                case "score":
                case "scores":
                    await ScoreAsync(message, guildId, game);
                    break;

                case "stop":
                case "end":
                    await StopAsync(message, guildId, game);
                    break;

                case "help":
                default:
                    await HelpAsync(message);
                    break;
            }
        }

        private static async Task HelpAsync(SocketMessage message)
        {
            await message.Channel.SendMessageAsync(
                "**Trivia commands**\n" +
                "- `!trivia start` — start a server-wide trivia game\n" +
                "- `!trivia next` — post next question\n" +
                "- `!trivia answer A|B|C|D` — answer current question\n" +
                "- `!trivia score` — show leaderboard\n" +
                "- `!trivia stop` — stop game & clear current question\n"
            );
        }

        private static async Task StartAsync(SocketMessage message, ulong guildId, TriviaGame game)
        {
            await game.Gate.WaitAsync();
            try
            {
                if (game.IsRunning)
                {
                    await message.Channel.SendMessageAsync("Trivia is already running. Use `!trivia next`.");
                    return;
                }

                game.IsRunning = true;
                game.Scores.Clear();
                game.Current = null;

                await message.Channel.SendMessageAsync(
                    "✅ **Trivia started (server-wide)!**\n" +
                    "Use `!trivia next` to post a question.\n" +
                    "Answer with `!trivia answer A|B|C|D`."
                );
            }
            finally { game.Gate.Release(); }
        }

        private static async Task StopAsync(SocketMessage message, ulong guildId, TriviaGame game)
        {
            await game.Gate.WaitAsync();
            try
            {
                if (!game.IsRunning)
                {
                    await message.Channel.SendMessageAsync("Trivia isn’t running.");
                    return;
                }

                game.IsRunning = false;
                game.Current = null;

                await message.Channel.SendMessageAsync("🛑 Trivia stopped. Use `!trivia start` to begin again.");
            }
            finally { game.Gate.Release(); }
        }

        private static async Task NextAsync(SocketMessage message, ulong guildId, TriviaGame game)
        {
            await game.Gate.WaitAsync();
            try
            {
                if (!game.IsRunning)
                {
                    await message.Channel.SendMessageAsync("Trivia isn’t running. Use `!trivia start` first.");
                    return;
                }

                // Fetch one question from OpenTDB via your wired service
                var api = Bot.BotInstance.Services.TriviaApi;
                var questions = await api.GetQuestionsAsync(amount: 1);

                if (questions.Count == 0)
                {
                    await message.Channel.SendMessageAsync("❌ No questions returned from OpenTDB.");
                    return;
                }

                var q = questions[0];

                // Build answer options and shuffle them
                var correct = Html(q.correct_answer);
                var options = new List<string>();

                if (q.incorrect_answers != null)
                    options.AddRange(q.incorrect_answers.Select(Html));

                options.Add(correct);
                Shuffle(options);

                var correctIndex = options.FindIndex(x => string.Equals(x, correct, StringComparison.Ordinal));
                game.Current = new TriviaQuestionState
                {
                    AskedAtUtc = DateTimeOffset.UtcNow,
                    Question = Html(q.question),
                    Category = Html(q.category),
                    Difficulty = Html(q.difficulty),
                    Options = options,
                    CorrectIndex = correctIndex
                };

                // Reset who answered for this question
                game.AnsweredUserIds.Clear();

                var letters = new[] { "A", "B", "C", "D" };

                // If boolean question, you'll get 2 options — handle both 2/4 safely
                var lines = options
                    .Select((opt, i) => $"**{letters[i]}**. {opt}")
                    .ToArray();

                var embed = new EmbedBuilder()
                    .WithTitle("🧠 Trivia Question")
                    .WithDescription($"**{game.Current.Question}**\n\n{string.Join("\n", lines)}")
                    .WithFooter($"Guild-wide game • Answer: !trivia answer A/B/C/D")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                if (!string.IsNullOrWhiteSpace(game.Current.Category) || !string.IsNullOrWhiteSpace(game.Current.Difficulty))
                    embed.AddField("Info", $"{game.Current.Category} • {game.Current.Difficulty}", inline: true);

                await message.Channel.SendMessageAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await message.Channel.SendMessageAsync($"❌ Failed to fetch trivia question: `{ex.Message}`");
            }
            finally { game.Gate.Release(); }
        }

        private static async Task AnswerAsync(SocketMessage message, ulong guildId, TriviaGame game, string[] args)
        {
            if (message.Author.IsBot) return;

            await game.Gate.WaitAsync();
            try
            {
                if (!game.IsRunning)
                {
                    await message.Channel.SendMessageAsync("Trivia isn’t running. Use `!trivia start`.");
                    return;
                }

                if (game.Current == null)
                {
                    await message.Channel.SendMessageAsync("No active question. Use `!trivia next`.");
                    return;
                }

                var userId = message.Author.Id;

                // One answer per user per question (simple anti-spam)
                if (!game.AnsweredUserIds.Add(userId))
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} you already answered this question.");
                    return;
                }

                if (args.Length == 0)
                {
                    await message.Channel.SendMessageAsync("Answer with `!trivia answer A` (or B/C/D).");
                    return;
                }

                var raw = args[0].Trim().ToUpperInvariant();
                int pick = raw switch
                {
                    "A" => 0,
                    "B" => 1,
                    "C" => 2,
                    "D" => 3,
                    _ => -1
                };

                if (pick < 0 || pick >= game.Current.Options.Count)
                {
                    await message.Channel.SendMessageAsync("Invalid choice. Use A/B/C/D.");
                    return;
                }

                var isCorrect = pick == game.Current.CorrectIndex;

                if (isCorrect)
                {
                    game.Scores.AddOrUpdate(userId, 1, (_, old) => old + 1);
                    await message.Channel.SendMessageAsync($"✅ {message.Author.Mention} **Correct!** (+1)");
                }
                else
                {
                    var correctLetter = new[] { "A", "B", "C", "D" }[game.Current.CorrectIndex];
                    var correctText = game.Current.Options[game.Current.CorrectIndex];
                    await message.Channel.SendMessageAsync($"❌ {message.Author.Mention} nope — correct was **{correctLetter}**. {correctText}");
                }
            }
            finally { game.Gate.Release(); }
        }

        private static async Task ScoreAsync(SocketMessage message, ulong guildId, TriviaGame game)
        {
            await game.Gate.WaitAsync();
            try
            {
                if (game.Scores.Count == 0)
                {
                    await message.Channel.SendMessageAsync("No scores yet.");
                    return;
                }

                var guild = (message.Channel as SocketGuildChannel)!.Guild;

                var top = game.Scores
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .Take(10)
                    .ToList();

                var lines = new List<string>();
                int rank = 1;

                foreach (var (userId, score) in top)
                {
                    var user = guild.GetUser(userId);
                    var name = user?.Mention ?? $"`{userId}`";
                    lines.Add($"**{rank}.** {name} — **{score}**");
                    rank++;
                }

                await message.Channel.SendMessageAsync("🏆 **Trivia Leaderboard (Top 10)**\n" + string.Join("\n", lines));
            }
            finally { game.Gate.Release(); }
        }

        private static string Html(string? s) => WebUtility.HtmlDecode(s ?? string.Empty);

        private static readonly Random _rng = new();

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private sealed class TriviaGame
        {
            public bool IsRunning { get; set; }
            public TriviaQuestionState? Current { get; set; }

            public ConcurrentDictionary<ulong, int> Scores { get; } = new();
            public HashSet<ulong> AnsweredUserIds { get; } = new();

            public SemaphoreSlim Gate { get; } = new(1, 1);
        }

        private sealed class TriviaQuestionState
        {
            public DateTimeOffset AskedAtUtc { get; set; }
            public string Question { get; set; } = "";
            public string Category { get; set; } = "";
            public string Difficulty { get; set; } = "";

            public List<string> Options { get; set; } = new();
            public int CorrectIndex { get; set; }
        }
    }
}