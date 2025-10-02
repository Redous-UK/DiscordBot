

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Discord.WebSocket;

public class RestartCommand : ILegacyCommand
{
    public string Name => "restart";
    public string Description => "Admin only. Restarts the bot’s Render service.";

    public string Category => "⚙️ Settings & Config";

    public async Task ExecuteAsync(SocketMessage message, string[] args)
    {
        if (message.Channel is not SocketTextChannel ch)
        {
            await message.Channel.SendMessageAsync("Run this in a server channel.");
            return;
        }
        var member = ch.GetUser(message.Author.Id);
        if (member == null || !member.GuildPermissions.Administrator)
        {
            await ch.SendMessageAsync("🚫 Admins only.");
            return;
        }

        var serviceId = Environment.GetEnvironmentVariable("RENDER_SERVICE_ID");
        var apiKey = Environment.GetEnvironmentVariable("RENDER_API_KEY");
        if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(apiKey))
        {
            await ch.SendMessageAsync("⚠️ Missing RENDER_SERVICE_ID or RENDER_API_KEY env vars.");
            return;
        }

        await ch.SendMessageAsync("🔄 Requesting restart…");
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var resp = await http.PostAsync(
                $"https://api.render.com/v1/services/{serviceId}/restart",
                content: null
            );

            if (resp.IsSuccessStatusCode)
                await ch.SendMessageAsync("✅ Restart requested. Give it a moment to cycle.");
            else
                await ch.SendMessageAsync($"⚠️ Restart API returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex)
        {
            await ch.SendMessageAsync($"❌ Error calling Render API: {ex.Message}");
        }
    }
}