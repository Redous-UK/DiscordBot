using MyDiscordBot.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyDiscordBot.Services
{
    public class ReminderService : IDisposable
    {
        private readonly object _sync = new();
        private readonly string _dbPath;

        // userId -> reminders
        private Dictionary<ulong, List<Reminder>> _store = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private bool _disposed;

        public ReminderService(string? dbPath = null)
        {
            var dataDir = ResolveDataDir();
            var target = dbPath ?? Path.Combine(dataDir, "reminders.json");
            var legacy = Path.Combine(AppContext.BaseDirectory, "reminders.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(legacy) && !File.Exists(target))
                {
                    File.Move(legacy, target);
                }
            }
            catch { /* best-effort migration */ }

            _dbPath = target;
            Load();
        }

        // NEW preferred overload (guild/channel + UTC + TimeSpan repeat)
        public Reminder AddReminder(
            ulong guildId,
            ulong channelId,
            ulong userId,
            string text,
            DateTimeOffset dueAtUtc,
            TimeSpan? repeatEvery)
        {
            var r = new Reminder
            {
                GuildId = guildId,
                ChannelId = channelId,
                UserId = userId,
                Message = text ?? string.Empty,
                DueAtUtc = dueAtUtc.ToUniversalTime(),
                RepeatMinutes = repeatEvery.HasValue ? (int?)Math.Max(1, (int)repeatEvery.Value.TotalMinutes) : null
            };

            lock (_sync)
            {
                if (!_store.TryGetValue(userId, out var list))
                {
                    list = new List<Reminder>();
                    _store[userId] = list;
                }
                list.Add(r);
                Save_NoLock();
            }

            return r;
        }

        // LEGACY overload kept for compatibility. Internally forwards to the new one.
        public Reminder AddReminder(ulong userId, DateTime timeUtc, string message, int? repeatMinutes = null)
        {
            if (timeUtc.Kind == DateTimeKind.Local) timeUtc = timeUtc.ToUniversalTime();
            var due = new DateTimeOffset(DateTime.SpecifyKind(timeUtc, DateTimeKind.Utc));
            return AddReminder(
                guildId: 0UL,
                channelId: 0UL,
                userId: userId,
                text: message,
                dueAtUtc: due,
                repeatEvery: repeatMinutes.HasValue ? TimeSpan.FromMinutes(Math.Max(1, repeatMinutes.Value)) : (TimeSpan?)null
            );
        }

        // Pop and/or reschedule all due reminders across users (UTC-based).
        public List<Reminder> PopDueForDelivery(DateTimeOffset? nowUtc = null)
        {
            nowUtc ??= DateTimeOffset.UtcNow;
            var dueAll = new List<Reminder>();

            lock (_sync)
            {
                foreach (var kv in _store)
                {
                    var list = kv.Value;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (r.DueAtUtc <= nowUtc.Value)
                        {
                            dueAll.Add(r);

                            if (r.RepeatMinutes is int minutes && minutes > 0)
                            {
                                var step = TimeSpan.FromMinutes(minutes);
                                var next = r.DueAtUtc + step;
                                while (next <= nowUtc.Value) next += step;
                                r.DueAtUtc = next;
                            }
                            else
                            {
                                list.RemoveAt(i);
                                i--;
                            }
                        }
                    }
                }
                Save_NoLock();
            }

            return dueAll.OrderBy(r => r.DueAtUtc).ToList();
        }

        // Back-compat API (kept if existing code expects this shape)
        public Dictionary<ulong, List<Reminder>> PopDueRemindersForAll(DateTime? nowUtc = null)
        {
            var now = nowUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(nowUtc.Value, DateTimeKind.Utc)) : DateTimeOffset.UtcNow;
            var result = new Dictionary<ulong, List<Reminder>>();

            lock (_sync)
            {
                foreach (var kv in _store)
                {
                    var userId = kv.Key;
                    var list = kv.Value;
                    List<Reminder>? due = null;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (r.DueAtUtc <= now)
                        {
                            (due ??= new List<Reminder>()).Add(r);

                            if (r.RepeatMinutes is int minutes && minutes > 0)
                            {
                                var step = TimeSpan.FromMinutes(minutes);
                                var next = r.DueAtUtc + step;
                                while (next <= now) next += step;
                                r.DueAtUtc = next;
                            }
                            else
                            {
                                list.RemoveAt(i);
                                i--;
                            }
                        }
                    }

                    if (due != null && due.Count > 0)
                        result[userId] = due;
                }

                Save_NoLock();
            }

            return result;
        }

        public bool RemoveReminder(ulong userId, Guid reminderId)
        {
            lock (_sync)
            {
                if (_store.TryGetValue(userId, out var list))
                {
                    var removed = list.RemoveAll(r => r.Id == reminderId) > 0;
                    if (removed) Save_NoLock();
                    return removed;
                }
                return false;
            }
        }

        public List<Reminder> GetReminders(ulong userId)
        {
            lock (_sync)
            {
                if (_store.TryGetValue(userId, out var list))
                    return list.OrderBy(r => r.DueAtUtc).ToList();
                return new List<Reminder>();
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_dbPath)) { _store = new(); return; }
                var json = File.ReadAllText(_dbPath);

                // Try new format first
                var data = JsonSerializer.Deserialize<Dictionary<ulong, List<Reminder>>>(json, _jsonOptions);
                if (data != null)
                {
                    _store = data;
                    return;
                }
            }
            catch { /* fall through to legacy */ }

            // Legacy fallback: old model had DateTime Time; map to DueAtUtc
            try
            {
                var json = File.ReadAllText(_dbPath);
                var legacy = JsonSerializer.Deserialize<Dictionary<ulong, List<OldReminder>>>(json, _jsonOptions) ?? new();
                _store = legacy.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(o => new Reminder
                    {
                        Id = o.Id,
                        GuildId = 0UL,
                        ChannelId = 0UL,
                        UserId = kv.Key,
                        Message = o.Message ?? string.Empty,
                        DueAtUtc = o.Time.Kind == DateTimeKind.Utc ? new DateTimeOffset(o.Time) : new DateTimeOffset(o.Time.ToUniversalTime()),
                        RepeatMinutes = o.RepeatMinutes
                    }).ToList()
                );
                Save_NoLock(); // persist in the new shape
            }
            catch
            {
                _store = new();
            }
        }

        private void Save_NoLock()
        {
            var json = JsonSerializer.Serialize(_store, _jsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            var tmp = _dbPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_dbPath)) { try { File.Replace(tmp, _dbPath, null); } catch { File.Delete(_dbPath); File.Move(tmp, _dbPath); } }
            else File.Move(tmp, _dbPath);
        }

        private static string ResolveDataDir()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("DATA_DIR"),
                Environment.GetEnvironmentVariable("RENDER_DISK_PATH"),
                "/data",
                "/var/data"
            };

            foreach (var p in candidates)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try { Directory.CreateDirectory(p); return p; } catch { }
            }
            return AppContext.BaseDirectory;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                lock (_sync) { Save_NoLock(); }
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        // Legacy DTO for migration
        private class OldReminder
        {
            public Guid Id { get; set; }
            public DateTime Time { get; set; }
            public string? Message { get; set; }
            public int? RepeatMinutes { get; set; }
        }
    }
}