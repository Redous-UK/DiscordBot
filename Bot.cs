using Discord;
using Discord.Commands;
using Discord.WebSocket;
using MyDiscordBot.Commands;
using MyDiscordBot.Models;
using MyDiscordBot.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyDiscordBot
{
    public class Bot : IDisposable
    {
        // --- Lifecycle/guards ---
        private volatile bool _didInit = false;
        private volatile bool _commandsLoaded = false;
        private volatile bool _birthdayLoopStarted = false;

        // prevent concurrent CheckBirthdays runs (future-proofing)
        private readonly SemaphoreSlim _birthdayCheckGate = new(1, 1);

        // dedupe birthday posts per day
        private readonly HashSet<string> _birthdaySentToday = new();
        private DateTime _lastBirthdayResetDate = DateTime.MinValue;

        // --- Services ---
        public ReminderService ReminderService { get; }

        // --- Discord client & config ---
        public DiscordSocketClient _client = null!;
        private string _token = null!;
        private string _prefix = "!";

        // --- Commands & settings ---
        private readonly Dictionary<string, ILegacyCommand> _legacyCommands = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<ulong, GuildSettings> _guildSettings = new();
        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new() { WriteIndented = true };

        // data files (persisted to disk)
        private static readonly string DataDir = ResolveDataDir();
        private static readonly string SettingsFile = Path.Combine(DataDir, "guild-settings.json");
        private static readonly string BirthdaysFile = Path.Combine(DataDir, "birthdays.json");

        public static Bot BotInstance { get; private set; } = null!;

        public Bot(ReminderService reminderService)
        {
            ReminderService = reminderService;
        }

        public DiscordSocketClient GetClient() => _client;

        public List<ILegacyCommand> GetAllLegacyCommands() => _legacyCommands.Values.ToList();

        public async Task RunAsync()
        {
            BotInstance = this;

            _token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_token))
                throw new InvalidOperationException("DISCORD_TOKEN env var is missing.");

            _prefix = Environment.GetEnvironmentVariable("PREFIX") ?? "!";

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                    | GatewayIntents.GuildMessages
                    | GatewayIntents.MessageContent
                    | GatewayIntents.GuildMembers
                    | GatewayIntents.GuildMessageReactions
                    | GatewayIntents.GuildVoiceStates
                    | GatewayIntents.GuildPresences
            });

            // Core logs + event wiring
            _client.Log += Log;
            _client.MessageReceived += HandleMessageAsync;

            _client.UserJoined += HandleUserJoinedAsync;
            _client.UserLeft += HandleUserLeftAsync;
            _client.UserVoiceStateUpdated += HandleVoiceStateAsync;
            _client.PresenceUpdated += HandlePresenceUpdatedAsync;
            _client.ReactionAdded += HandleReactionAddedAsync;

            // Guild join/leave visibility (helps debug invite issues)
            _client.JoinedGuild += g =>
            {
                Console.WriteLine($"[GUILD] Joined: {g.Name} ({g.Id})");
                return Task.CompletedTask;
            };
            _client.LeftGuild += g =>
            {
                Console.WriteLine($"[GUILD] Left: {g.Name} ({g.Id})");
                return Task.CompletedTask;
            };

            // Single-run init after Ready
            _client.Ready += OnClientReadyOnce;

            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();

            // keep alive
            await Task.Delay(Timeout.Infinite);
        }

        // One-time init after the gateway is ready
        private async Task OnClientReadyOnce()
        {
            if (_didInit) return;
            lock (this)
            {
                if (_didInit) return;
                _didInit = true;
            }
            _client.Ready -= OnClientReadyOnce;

            Console.WriteLine("[READY] init start");

            EnsureDataDir();
            LoadSettings();
            LoadLegacyCommandsOnce();

            foreach (var guild in _client.Guilds)
            {
                var settings = GetSettings(guild.Id);

                if (!string.IsNullOrEmpty(settings.Nickname))
                {
                    var botUser = guild.GetUser(_client.CurrentUser.Id);
                    if (botUser != null)
                    {
                        try { await botUser.ModifyAsync(p => p.Nickname = settings.Nickname); } catch { }
                    }
                }

                LogMessage(guild.Id, $"Connected to guild: {guild.Name} ({guild.Id})", LogCategory.Log);
            }

            if (!_birthdayLoopStarted)
            {
                _birthdayLoopStarted = true;
                _ = RepeatBirthdayCheck();
            }

            Console.WriteLine("[READY] init end");
        }

        // ------------- Command pipeline with per-guild debug logging -------------

        private async Task HandleMessageAsync(SocketMessage message)
        {
            // Ignore bots/system/empty
            if (message.Author.IsBot || string.IsNullOrWhiteSpace(message.Content))
                return;

            if (message is not SocketUserMessage userMessage)
                return;

            int argPos = 0;
            if (!userMessage.HasStringPrefix(_prefix, ref argPos))
                return;

            var content = userMessage.Content.Substring(argPos).Trim();

            string commandName;
            string argRest = string.Empty;

            int firstSpace = content.IndexOf(' ');
            if (firstSpace < 0)
            {
                commandName = content.ToLowerInvariant();
            }
            else
            {
                commandName = content.Substring(0, firstSpace).ToLowerInvariant();
                argRest = content.Substring(firstSpace + 1);
            }

            bool debug = DebugOn(message);
            var ctx = debug ? FormatContext(message) : string.Empty;
            var sw = debug ? Stopwatch.StartNew() : null;

            if (!_legacyCommands.TryGetValue(commandName, out var command))
            {
                if (debug) Console.WriteLine($"[CMD] unknown '{commandName}' args=\"{argRest}\" | {ctx}");
                // Route unknowns to 'Other' category for guild logs
                var gid = (message.Channel as SocketGuildChannel)?.Guild.Id;
                if (gid.HasValue)
                    LogMessage(gid.Value, $"Unknown command '{commandName}' args=\"{argRest}\"");
                return;
            }

            if (debug) Console.WriteLine($"[CMD] start   '{commandName}' args=\"{argRest}\" | {ctx}");
            try
            {
                var args = string.IsNullOrWhiteSpace(argRest)
                    ? Array.Empty<string>()
                    : argRest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                await command.ExecuteAsync(message, args);

                if (debug && sw is not null)
                {
                    sw.Stop();
                    Console.WriteLine($"[CMD] ok      '{commandName}' {sw.ElapsedMilliseconds}ms | {ctx}");
                }
            }
            catch (Exception ex)
            {
                if (debug && sw is not null)
                {
                    sw.Stop();
                    Console.WriteLine($"[CMD] fail    '{commandName}' {sw.ElapsedMilliseconds}ms | {ctx}\n{ex}");
                }
            }
        }

        private static bool DebugOn(SocketMessage msg)
        {
            var guildId = (msg.Channel as SocketGuildChannel)?.Guild.Id;
            return guildId.HasValue && GetDebugMode(guildId.Value);
        }

        private static string FormatContext(SocketMessage msg)
        {
            var guild = (msg.Channel as SocketGuildChannel)?.Guild;
            var guildPart = guild is null ? "DM" : $"{guild.Name}({guild.Id})";
            var channelName = (msg.Channel as SocketGuildChannel)?.Name ?? "DM";
            var channelPart = $"{channelName}({msg.Channel.Id})";
            var user = msg.Author;
            var userPart = $"{user.Username}#{user.Discriminator}({user.Id})";
            return $"guild={guildPart} chan={channelPart} user={userPart}";
        }

        // ------------------- Settings (persisted to DATA_DIR) --------------------

        private static string ResolveDataDir()
        {
            // prefer explicit DATA_DIR, then /data (Render), then /var/data, then app dir
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("DATA_DIR"),
                "/data",
                "/var/data",
                AppContext.BaseDirectory
            };

            foreach (var p in candidates)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    Directory.CreateDirectory(p);
                    return p;
                }
                catch { /* try next */ }
            }

            return AppContext.BaseDirectory;
        }

        private static void EnsureDataDir()
        {
            try { Directory.CreateDirectory(DataDir); } catch { }
        }

        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    _guildSettings = JsonSerializer.Deserialize<Dictionary<ulong, GuildSettings>>(json)
                                     ?? new Dictionary<ulong, GuildSettings>();
                }
                else
                {
                    _guildSettings = new Dictionary<ulong, GuildSettings>();
                    SafeWrite(SettingsFile, JsonSerializer.Serialize(_guildSettings, CachedJsonSerializerOptions));
                }
            }
            catch
            {
                _guildSettings = new Dictionary<ulong, GuildSettings>();
            }
        }

        private static void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_guildSettings, CachedJsonSerializerOptions);
                SafeWrite(SettingsFile, json);
            }
            catch { /* best-effort */ }
        }

        private static void SafeWrite(string path, string json)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, true);
        }

        public static void SaveGuildSettings() => SaveSettings();

        public static GuildSettings GetSettings(ulong guildId)
        {
            if (!_guildSettings.ContainsKey(guildId))
                _guildSettings[guildId] = new GuildSettings();
            return _guildSettings[guildId];
        }

        public static bool IsLogCategoryEnabled(ulong guildId, LogCategory category)
            => GetSettings(guildId).LogCategories.Contains(category.ToString());

        public static bool GetDebugMode(ulong guildId)
            => GetSettings(guildId).DebugEnabled;

        public static void SetDebugMode(ulong guildId, bool enabled)
        {
            GetSettings(guildId).DebugEnabled = enabled;
            SaveSettings();
        }

        public static bool ToggleLogCategory(ulong guildId, LogCategory category)
        {
            var settings = GetSettings(guildId);
            if (settings.LogCategories.Contains(category.ToString()))
                settings.LogCategories.Remove(category.ToString());
            else
                settings.LogCategories.Add(category.ToString());

            SaveSettings();
            return settings.LogCategories.Contains(category.ToString());
        }

        private static void LogMessage(ulong guildId, string message, LogCategory category = LogCategory.Other)
        {
            if (IsLogCategoryEnabled(guildId, category))
                Console.WriteLine($"[{category}] {message}");
        }

        // -------------------------- Birthdays loop -------------------------------

        private async Task RepeatBirthdayCheck()
        {
            while (true)
            {
                var nextRunLocalMidnight = DateTime.Today.AddDays(1);
                var delay = nextRunLocalMidnight - DateTime.Now;
                if (delay < TimeSpan.FromSeconds(1))
                    delay = TimeSpan.FromSeconds(1);

                await Task.Delay(delay);
                await CheckBirthdaysInternal();
            }
        }

        private async Task CheckBirthdaysInternal()
        {
            await _birthdayCheckGate.WaitAsync();
            try
            {
                ResetBirthdaySentIfNewDay();

                if (!File.Exists(BirthdaysFile))
                {
                    var empty = new Dictionary<string, BirthdayCommand.BirthdayEntry>();
                    SafeWrite(BirthdaysFile, JsonSerializer.Serialize(empty, CachedJsonSerializerOptions));
                    return;
                }

                var json = File.ReadAllText(BirthdaysFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, BirthdayCommand.BirthdayEntry>>(json, CachedJsonSerializerOptions);
                if (data == null || data.Count == 0) return;

                var today = DateTime.Today;

                foreach (var kv in data)
                {
                    var key = kv.Key;
                    var entry = kv.Value;

                    var parts = key.Split('-');
                    if (parts.Length != 2 || !ulong.TryParse(parts[0], out var guildId) || !ulong.TryParse(parts[1], out var userId))
                        continue;

                    if (entry.Date.Month != today.Month || entry.Date.Day != today.Day) continue;

                    var guild = _client.GetGuild(guildId);
                    var user = guild?.GetUser(userId);
                    var channel = guild?.TextChannels.FirstOrDefault(c =>
                        guild.CurrentUser.GetPermissions(c).SendMessages &&
                        (GetSettings(guildId).BirthdayChannelId == 0 || c.Id == GetSettings(guildId).BirthdayChannelId));

                    if (guild == null || user == null || channel == null) continue;

                    var sentKey = $"{guildId}:{userId}:{DateTime.UtcNow:yyyy-MM-dd}";
                    if (_birthdaySentToday.Contains(sentKey)) continue;

                    await channel.SendMessageAsync($"🎉 Happy Birthday {user.Mention}!");
                    _birthdaySentToday.Add(sentKey);
                }
            }
            finally
            {
                _birthdayCheckGate.Release();
            }
        }

        private void ResetBirthdaySentIfNewDay()
        {
            var today = DateTime.UtcNow.Date;
            if (_lastBirthdayResetDate != today)
            {
                _birthdaySentToday.Clear();
                _lastBirthdayResetDate = today;
            }
        }

        // ----------------------------- Other events ------------------------------

        private void LoadLegacyCommandsOnce()
        {
            if (_commandsLoaded) return;
            _commandsLoaded = true;

            var commandTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ILegacyCommand).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in commandTypes)
            {
                // skip anything explicitly marked obsolete
                if (type.GetCustomAttribute<ObsoleteAttribute>() != null) continue;

                if (Activator.CreateInstance(type) is ILegacyCommand instance)
                {
                    _legacyCommands[instance.Name.ToLowerInvariant()] = instance;
                }
            }

            // optional: log what was loaded per guild under the Register category
            foreach (var g in _client.Guilds)
                foreach (var key in _legacyCommands.Keys)
                    LogMessage(g.Id, $"Loaded legacy command: {_prefix}{key}", LogCategory.Register);
        }

        private Task HandleUserJoinedAsync(SocketGuildUser user)
        {
            if (IsLogCategoryEnabled(user.Guild.Id, LogCategory.Register))
                Console.WriteLine($"[JOIN] {user.Username} joined {user.Guild.Name}");
            return Task.CompletedTask;
        }

        private Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
        {
            if (IsLogCategoryEnabled(guild.Id, LogCategory.Register))
                Console.WriteLine($"[LEAVE] {user.Username} left {guild.Name}");
            return Task.CompletedTask;
        }

        private Task HandleVoiceStateAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            Console.WriteLine($"[VOICE] {user.Username} moved from {before.VoiceChannel?.Name ?? "None"} to {after.VoiceChannel?.Name ?? "None"}");
            return Task.CompletedTask;
        }

        private Task HandlePresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
        {
            if (user is SocketGuildUser gUser)
            {
                ulong guildId = gUser.Guild.Id;
                if (IsLogCategoryEnabled(guildId, LogCategory.Presence))
                    Console.WriteLine($"[PRESENCE] {user.Username} went from {before.Status} to {after.Status}");
            }
            return Task.CompletedTask;
        }

        private Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            if (reaction.User.IsSpecified && reaction.Channel is SocketGuildChannel guildChannel)
            {
                var guildId = guildChannel.Guild.Id;
                if (IsLogCategoryEnabled(guildId, LogCategory.Reaction))
                    Console.WriteLine($"[REACTION] {reaction.UserId} reacted with {reaction.Emote.Name} in channel {channel.Id}");
            }
            return Task.CompletedTask;
        }

        private Task Log(LogMessage msg)
        {
            // Optionally suppress REST rate-limit noise:
            // if (msg.Source == "Rest") return Task.CompletedTask;

            Console.WriteLine("[LOG] " + msg.ToString());
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            ReminderService?.Dispose();
            _birthdayCheckGate?.Dispose();
        }
    }
}