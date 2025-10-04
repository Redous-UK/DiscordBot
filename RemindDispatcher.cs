// Uncomment the following line if you're still using the legacy ReminderService API
// #define USE_LEGACY_REMINDER_SERVICE

using Discord;
using Discord.WebSocket;
using System;
using System.Threading;
using System.Threading.Tasks;

using MyDiscordBot.Services; // ReminderService

namespace MyDiscordBot.Background
{
    /// <summary>
    /// Periodically polls the ReminderService and delivers due reminders.
    /// Gate it with IS_DISPATCHER=true on exactly ONE Render service to avoid double-firing.
    /// </summary>
    public static class ReminderDispatcher
    {
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static DiscordSocketClient? _client;
        private static ReminderService? _service;
        private static readonly SemaphoreSlim _gate = new(1, 1);

        private static TimeSpan PollInterval => TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("REMINDER_POLL_SECONDS"), out var s) ? Math.Max(2, s) : 5);

        // A small window so we don't miss items that are due right at the tick boundary
        private static TimeSpan PollWindow => TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("REMINDER_POLL_WINDOW"), out var w) ? Math.Clamp(w, 0, 10) : 2);

        public static void Start(DiscordSocketClient client, ReminderService service, Action<string>? log = null)
        {
            if (Environment.GetEnvironmentVariable("IS_DISPATCHER") == "false")
            {
                log?.Invoke("ReminderDispatcher: disabled by IS_DISPATCHER=false");
                return;
            }

            if (_cts != null) { log?.Invoke("ReminderDispatcher: already running"); return; }

            _client = client;
            _service = service;
            _cts = new CancellationTokenSource();

            log?.Invoke($"ReminderDispatcher START — Poll={PollInterval.TotalSeconds}s Window={PollWindow.TotalSeconds}s");

            _loopTask = Task.Run(async () =>
            {
                var timer = new PeriodicTimer(PollInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(_cts.Token))
                    {
                        try { await SweepOnceAsync(log, _cts.Token); }
                        catch (OperationCanceledException) { }
                        catch (Exception ex) { log?.Invoke($"Sweep error: {ex.Message}"); }
                    }
                }
                catch (OperationCanceledException) { }
            }, _cts.Token);
        }

        public static void Stop(Action<string>? log = null)
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            log?.Invoke("ReminderDispatcher STOP");
        }

        public static async Task SweepOnceAsync(Action<string>? log = null, CancellationToken ct = default)
        {
            if (_client == null || _service == null) return;

            await _gate.WaitAsync(ct);
            try
            {
#if USE_LEGACY_REMINDER_SERVICE
                var now = DateTime.UtcNow.Add(PollWindow);
                var byUser = _service.PopDueRemindersForAll(now);
                if (byUser.Count == 0) return;
                log?.Invoke($"Due reminders: {byUser.Count} user buckets");

                foreach (var kv in byUser)
                {
                    var userId = kv.Key;
                    var list = kv.Value;

                    // Legacy model has no ChannelId/GuildId; deliver via DM
                    var user = _client.GetUser(userId);
                    IDMChannel? dm = null;
                    if (user != null)
                    {
                        try { dm = await user.CreateDMChannelAsync(); }
                        catch (Exception ex) { log?.Invoke($"DM open failed for {userId}: {ex.Message}"); }
                    }

                    foreach (var r in list)
                    {
                        try
                        {
                            if (dm != null)
                                await dm.SendMessageAsync($"⏰ <@{userId}> {r.Message}");
                        }
                        catch (Exception ex)
                        {
                            log?.Invoke($"Send failed (user {userId}): {ex.Message}");
                        }
                    }
                }
#else
                var now = DateTimeOffset.UtcNow + PollWindow;
                // Preferred: upgraded service exposes a PopDueForDelivery-style method (see canvas upgrade)
                var due = _service.PopDueForDelivery(now);
                if (due.Count == 0) return;
                log?.Invoke($"Due reminders: {due.Count}");

                foreach (var r in due)
                {
                    IMessageChannel? channel = null;

                    // Try channel from the reminder first
                    if (r.ChannelId != 0)
                    {
                        channel = _client.GetChannel(r.ChannelId) as IMessageChannel;
                        if (channel == null && r.GuildId != 0)
                        {
                            var guild = _client.GetGuild(r.GuildId);
                            if (guild != null) channel = guild.GetTextChannel(r.ChannelId);
                        }
                    }

                    // Fallback to DM if channel not available
                    if (channel == null)
                    {
                        var user = _client.GetUser(r.UserId);
                        if (user != null)
                        {
                            try { channel = await user.CreateDMChannelAsync(); }
                            catch (Exception ex) { log?.Invoke($"DM open failed for {r.UserId}: {ex.Message}"); }
                        }
                    }

                    if (channel == null)
                    {
                        log?.Invoke($"No delivery channel for reminder {r.Id} (user {r.UserId}). Skipping.");
                        continue;
                    }

                    try
                    {
                        await channel.SendMessageAsync($"⏰ <@{r.UserId}> {r.Message}");
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"Send failed for reminder {r.Id}: {ex.Message}");
                    }
                }
#endif
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
