using MyDiscordBot.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using MyDiscordBot.Models;

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
            // DateTime is handled as ISO-8601; no custom converters needed.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private bool _disposed;

        public ReminderService(string? dbPath = null)
        {
            var dataDir = ResolveDataDir();
            var target = dbPath ?? Path.Combine(dataDir, "reminders.json");
            var legacy = Path.Combine(AppContext.BaseDirectory, "reminders.json");

            // One-time migration from legacy location to the persistent disk
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

        private static string ResolveDataDir()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("DATA_DIR"),         // e.g., "/data" (your current setting)
                Environment.GetEnvironmentVariable("RENDER_DISK_PATH"),  // Render exposes this for mounted disks
                "/data",                                               // common mount path
                "/var/data"                                            // another common choice
            };

            foreach (var p in candidates)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    Directory.CreateDirectory(p);
                    return p;
                }
                catch { /* try next candidate */ }
            }

            // Fallback to app directory if no disk is available
            return AppContext.BaseDirectory;
        }

        public List<Reminder> GetReminders(ulong userId)
        {
            lock (_sync)
            {
                if (_store.TryGetValue(userId, out var list))
                    return list.OrderBy(r => r.Time).ToList();
                return new List<Reminder>();
            }
        }

        public Reminder AddReminder(ulong userId, DateTime timeUtc, string message, int? repeatMinutes = null)
        {
            if (timeUtc.Kind == DateTimeKind.Local)
                timeUtc = timeUtc.ToUniversalTime();

            var r = new Reminder
            {
                Time = DateTime.SpecifyKind(timeUtc, DateTimeKind.Utc),
                Message = message ?? string.Empty,
                RepeatMinutes = (repeatMinutes is > 0) ? repeatMinutes : null
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

        public List<Reminder> PopDueReminders(ulong userId, DateTime? nowUtc = null)
        {
            nowUtc ??= DateTime.UtcNow;
            var due = new List<Reminder>();

            lock (_sync)
            {
                if (!_store.TryGetValue(userId, out var list) || list.Count == 0)
                    return due;

                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (r.Time <= nowUtc.Value)
                    {
                        due.Add(r);

                        if (r.RepeatMinutes is int minutes && minutes > 0)
                        {
                            var next = r.Time;
                            var step = TimeSpan.FromMinutes(minutes);
                            while (next <= nowUtc.Value) next = next.Add(step);
                            r.Time = next;
                        }
                        else
                        {
                            list.RemoveAt(i);
                            i--;
                        }
                    }
                }

                Save_NoLock();
            }

            return due.OrderBy(r => r.Time).ToList();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_dbPath))
                {
                    _store = new Dictionary<ulong, List<Reminder>>();
                    return;
                }

                var json = File.ReadAllText(_dbPath);
                var data = JsonSerializer.Deserialize<Dictionary<ulong, List<Reminder>>>(json, _jsonOptions);
                _store = data ?? new Dictionary<ulong, List<Reminder>>();
            }
            catch
            {
                _store = new Dictionary<ulong, List<Reminder>>();
            }
        }

        private void Save_NoLock()
        {
            var json = JsonSerializer.Serialize(_store, _jsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            File.WriteAllText(_dbPath, json);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                lock (_sync)
                {
                    Save_NoLock();
                }
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
