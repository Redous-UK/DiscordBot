using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyDiscordBot.Services
{
    public sealed class QnAApiService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public QnAApiService(HttpClient http, string baseUrl, string apiKey)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _apiKey = apiKey ?? string.Empty;
        }

        private HttpRequestMessage NewReq(HttpMethod method, string path)
        {
            var req = new HttpRequestMessage(method, _baseUrl + path);
            if (!string.IsNullOrWhiteSpace(_apiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return req;
        }

        public async Task<AskResult?> AskAsync(ulong guildId, ulong channelId, ulong askerId, string question, CancellationToken ct = default)
        {
            using var req = NewReq(HttpMethod.Post, "/qa/questions");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { guildId, channelId, askerId, question }, JsonOpts),
                Encoding.UTF8,
                "application/json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<AskResult>(json, JsonOpts);
        }

        public async Task<bool> AnswerAsync(ulong guildId, ulong questionId, ulong responderId, string answer, CancellationToken ct = default)
        {
            using var req = NewReq(HttpMethod.Post, $"/qa/questions/{questionId}/answers");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { guildId, responderId, answer }, JsonOpts),
                Encoding.UTF8,
                "application/json");

            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }

        public async Task<QuestionDetails?> GetQuestionAsync(ulong guildId, ulong questionId, CancellationToken ct = default)
        {
            using var req = NewReq(HttpMethod.Get, $"/qa/questions/{questionId}?guildId={guildId}");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<QuestionDetails>(json, JsonOpts);
        }

        public void Dispose()
        {
            // Intentionally empty: Bot owns HttpClient.
        }
    }

    // DTOs — adjust fields to match your API responses
    public sealed record AskResult(ulong QuestionId, string Status);
    public sealed record QuestionDetails(ulong QuestionId, string Question, ulong AskerId, string Status);
}