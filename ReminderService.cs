using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class ReminderService : IDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "reminders.json");
    private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private readonly object _sync = new();

    // userId -> list of reminders
    private Dictionary<ulong, List<ReminderEntry>> _reminders = new();

    private readonly CancellationTokenSource _cts = new();
    private static bool _loopStarted = false;
    private Task _loopTask;

    public ReminderService(DiscordSocketClient client)
    {
        _client = client;
        LoadReminders();

        if (!_loopStarted)
        {
            _loopStarted = true;
            _loopTask = Task.Run(() => ReminderLoopAsync(_cts.Token));
        }
    }

    public List<ReminderEntry> GetReminders(ulong userId)
    {
        lock (_sync)
        {
            if (_reminders.TryGetValue(userId, out var list))
                return list.OrderBy(r => r.Time).Select(r => new ReminderEntry { Message = r.Message, Time = r.Time }).ToList();
            return new List<ReminderEntry>();
        }
    }

    public void AddReminder(ulong userId, ReminderEntry entry)
    {
        // normalize time to UTC just in case
        if (entry.Time.Kind != DateTimeKind.Utc)
            entry.Time = DateTime.SpecifyKind(entry.Time, DateTimeKind.Utc);

        lock (_sync)
        {
            if (!_reminders.TryGetValue(userId, out var list))
            {
                list = new List<ReminderEntry>();
                _reminders[userId] = list;
            }
            list.Add(entry);
            SaveReminders();
        }
    }

    // Optional helper if you add a remove command later
    public bool RemoveReminderByIndex(ulong userId, int index)
    {
        lock (_sync)
        {
            if (_reminders.TryGetValue(userId, out var list) && index >= 0 && index < list.Count)
            {
                list.RemoveAt(index);
                if (list.Count == 0) _reminders.Remove(userId);
                SaveReminders();
                return true;
            }
        }
        return false;
    }

    private async Task ReminderLoopAsync(CancellationToken token)
    {
        // Check every 10 seconds; catch-up is automatic because we look for <= now
        while (!token.IsCancellationRequested)
        {
            bool changed = false;
            DateTime now = DateTime.UtcNow;

            List<(ulong userId, ReminderEntry entry)> due;
            lock (_sync)
            {
                due = _reminders
                    .SelectMany(kvp => kvp.Value
                        .Where(r => r.Time <= now)
                        .Select(r => (kvp.Key, r)))
                    .ToList();

                foreach (var (userId, entry) in due)
                {
                    _reminders[userId].Remove(entry);
                    if (_reminders[userId].Count == 0)
                        _reminders.Remove(userId);
                    changed = true;
                }
            }

            // Send after unlocking
            foreach (var (userId, entry) in due)
            {
                try
                {
                    var user = _client.GetUser(userId);
                    if (user != null)
                        await user.SendMessageAsync($":alarm_clock: Reminder: {entry.Message}");
                }
                catch
                {
                    // swallow send errors to keep loop alive
                }
            }

            if (changed)
            {
                // persist removals
                SaveReminders();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void LoadReminders()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _reminders = new Dictionary<ulong, List<ReminderEntry>>();
                return;
            }

            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<Dictionary<ulong, List<ReminderEntry>>>(json, _jsonOpts);
            _reminders = data ?? new Dictionary<ulong, List<ReminderEntry>>();

            // sanity: ensure UTC kind
            foreach (var list in _reminders.Values)
                foreach (var r in list)
                    if (r.Time.Kind != DateTimeKind.Utc)
                        r.Time = DateTime.SpecifyKind(r.Time, DateTimeKind.Utc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReminderService] Failed to load reminders: {ex.Message}");
            _reminders = new Dictionary<ulong, List<ReminderEntry>>();
        }
    }

    private void SaveReminders()
    {
        try
        {
            // Atomic write: write temp then replace
            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(_reminders, _jsonOpts);
            File.WriteAllText(tempPath, json);
            if (File.Exists(_filePath)) File.Delete(_filePath);
            File.Move(tempPath, _filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReminderService] Failed to save reminders: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loopTask?.Wait(2000); } catch { /* ignore */ }
        _cts.Dispose();
    }
}