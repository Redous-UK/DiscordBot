using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyDiscordBot.Services
{
    public sealed class OpenTdbService : IDisposable
    {
        private readonly HttpClient _http;

        public OpenTdbService(HttpClient http)
            => _http = http ?? throw new ArgumentNullException(nameof(http));

        public async Task<IReadOnlyList<TriviaQuestion>> GetQuestionsAsync(
            int amount = 10,
            CancellationToken ct = default)
        {
            var url = $"https://opentdb.com/api.php?amount={amount}";

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<OpenTdbResponse>(json);

            if (data == null || data.results == null)
                return Array.Empty<TriviaQuestion>();

            if (data.response_code != 0)
                throw new InvalidOperationException($"OpenTDB response_code={data.response_code}");

            // Decode HTML entities in all fields
            foreach (var q in data.results)
            {
                q.question = WebUtility.HtmlDecode(q.question);
                q.correct_answer = WebUtility.HtmlDecode(q.correct_answer);

                if (q.incorrect_answers != null)
                {
                    for (int i = 0; i < q.incorrect_answers.Count; i++)
                        q.incorrect_answers[i] = WebUtility.HtmlDecode(q.incorrect_answers[i]);
                }

                q.category = WebUtility.HtmlDecode(q.category);
                q.difficulty = WebUtility.HtmlDecode(q.difficulty);
            }

            return data.results;
        }

        public void Dispose()
        {
            // Bot owns HttpClient; don't dispose here
        }

        public sealed class OpenTdbResponse
        {
            public int response_code { get; set; }
            public List<TriviaQuestion>? results { get; set; }
        }

        public sealed class TriviaQuestion
        {
            public string category { get; set; } = "";
            public string type { get; set; } = "";
            public string difficulty { get; set; } = "";
            public string question { get; set; } = "";
            public string correct_answer { get; set; } = "";
            public List<string> incorrect_answers { get; set; } = new();
        }
    }
}