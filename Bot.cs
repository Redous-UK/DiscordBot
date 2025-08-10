using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DotNetEnv;
using MyDiscordBot.Commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyDiscordBot.Models;
using MyDiscordBot.Services;
using System.Diagnostics;

namespace MyDiscordBot
{
    public class Bot(ReminderService reminderService) : IDisposable
    {
        // --- Lifecycle/guards ---
        private volatile bool _didInit = false;
        private volatile bool _commandsLoaded = false;
        private volatile bool _birthdayLoopStarted = false;

        // prevent concurrent CheckBirthdays runs
        private readonly SemaphoreSlim _birthdayCheckGate = new(1, 1);

        // dedupe birthday posts per day
        private readonly HashSet<string> _birthdaySentToday = [];
        private DateTime _lastBirthdayResetDate = DateTime.MinValue;

        // --- Services ---
        private volatile bool _reminderLoopStarted = false;
        public ReminderService ReminderService { get; } = reminderService;// init client, register handlers, etc.

        // --- Discord client & config ---
        public DiscordSocketClient _client = null!;
        private string _token = null!;
        private string _prefix = "!";

        // --- Commands & settings ---
        private readonly Dictionary<string, ILegacyCommand> _legacyCommands = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<ulong, GuildSettings> _guildSettings = [];
        private const string SettingsFile = "guild-settings.json";

        public List<ILegacyCommand> GetAllLegacyCommands()
        {
            return [.. _legacyCommands.Values];
        }

        public static Bot BotInstance { get; private set; } = null!;

        public DiscordSocketClient GetClient()
        {
            return _client;
        }

        public async Task RunAsync()
        {
            BotInstance = this;

            Env.Load();
            _token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? null!;
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

            // Wire events ONCE here (outside Ready)
            _client.Log += Log;
            _client.MessageReceived += HandleMessageAsync;
            _client.UserJoined += HandleUserJoinedAsync;
            _client.UserLeft += HandleUserLeftAsync;
            _client.UserVoiceStateUpdated += HandleVoiceStateAsync;
            _client.PresenceUpdated += HandlePresenceUpdatedAsync;
            _client.ReactionAdded += HandleReactionAddedAsync;

            // Use a named handler so we can safely unsubscribe after first successful init
            _client.Ready += OnClientReadyOnce;

            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();

            // keep alive
            await Task.Delay(Timeout.Infinite);
        }

        // Update the OnClientReadyOnce method to match the expected delegate signature for the Ready event.
        private async Task<Task> OnClientReadyOnce()
        {
            if (_didInit) return Task.CompletedTask;
            lock (this)
            {
                if (_didInit) return Task.CompletedTask;
                _didInit = true;
            }
            _client.Ready -= OnClientReadyOnce;

            Console.WriteLine("[READY] Starting Bot Setup");

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

                if (!_reminderLoopStarted)
                {
                    _reminderLoopStarted = true;
                    _ = RepeatReminderDispatchAsync();
                }

            }

            if (!_birthdayLoopStarted)
            {
                _birthdayLoopStarted = true;
                _ = RepeatBirthdayCheck();
            }

            Console.WriteLine("[READY] Ending Bot Setup");
            return Task.CompletedTask;
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


        // Rename one of the duplicate methods to resolve the conflict
        private async Task CheckBirthdaysInternal()
        {
            ResetBirthdaySentIfNewDay();

            var today = DateTime.Today;
            var birthdayPath = Path.Combine(AppContext.BaseDirectory, "birthdays.json");
            if (!File.Exists(birthdayPath))
            {
                var emptyData = new Dictionary<string, BirthdayCommand.BirthdayEntry>();
                File.WriteAllText(birthdayPath, JsonSerializer.Serialize(emptyData, CachedJsonSerializerOptions));
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, BirthdayCommand.BirthdayEntry>>(File.ReadAllText(birthdayPath), CachedJsonSerializerOptions);
            if (data == null || data.Count == 0) return;

            foreach (var (key, entry) in data)
            {
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

        private async Task RepeatReminderDispatchAsync()
        {
            // poll every 15s; adjust if you like
            var interval = TimeSpan.FromSeconds(15);
            while (true)
            {
                await Task.Delay(interval);
                await DispatchDueRemindersOnceAsync();
            }
        }

        private async Task DispatchDueRemindersOnceAsync()
        {
            var batches = Program.ReminderService.PopDueRemindersForAll();

            foreach (var kv in batches)
            {
                var userId = kv.Key;
                var reminders = kv.Value;

                // Replace the problematic line with the following code to resolve the type mismatch issue:
                var user = _client.GetUser(userId) as IUser ?? await _client.Rest.GetUserAsync(userId);
                if (user == null) continue;

                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    foreach (var r in reminders)
                    {
                        await dm.SendMessageAsync($":alarm_clock: **Reminder:** {r.Message}  •  (due {r.Time:u})");
                    }
                }
                catch
                {
                    // DM blocked; nothing else to post to (we don’t store channel IDs)
                    Console.WriteLine($"[reminders] Could not DM user {userId}.");
                }
            }
        }

        private async Task<object> GetBirthdayCheckResultAsync()
        {
            await CheckBirthdaysInternal();
            return Task.CompletedTask;
        }

        // Fix the ambiguous call issue by renaming one of the duplicate methods.
        // The error occurs because there are two methods named `OnClientReadyOnce` in the class.
        // Rename one of the methods to resolve the ambiguity.

        /*private async Task<Task> OnClientReadyOnceRenamed()
        {
            if (_didInit) return Task.CompletedTask;
            lock (this)
            {
                if (_didInit) return Task.CompletedTask;
                _didInit = true;
            }
            _client.Ready -= OnClientReadyOnce;

            Console.WriteLine("[READY] init start");

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
            return Task.CompletedTask;
        }*/

        private void LoadLegacyCommandsOnce()
        {
            if (_commandsLoaded) return;
            _commandsLoaded = true;

            var commandTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ILegacyCommand).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in commandTypes)
            {
                if (type.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
                var instance = Activator.CreateInstance(type) as ILegacyCommand ?? null!;
                _legacyCommands[instance.Name.ToLower()] = instance;
            }

            foreach (var g in _client.Guilds)
                foreach (var kvp in _legacyCommands)
                    LogMessage(g.Id, $"Loaded legacy command: {_prefix}{kvp.Key}", LogCategory.Register);
        }

        private static void LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    _guildSettings = JsonSerializer.Deserialize<Dictionary<ulong, GuildSettings>>(File.ReadAllText(SettingsFile))
                                     ?? [];
                }
                catch
                {
                    _guildSettings = [];
                }
            }
            else
            {
                _guildSettings = [];
            }
        }

        // Add a static readonly field to cache the JsonSerializerOptions instance
        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new() { WriteIndented = true };

        // Update the SaveSettings method to use the cached instance
        private static void SaveSettings()
        {
            var json = JsonSerializer.Serialize(_guildSettings, CachedJsonSerializerOptions);
            File.WriteAllText(SettingsFile, json);
        }

        // Update the CheckBirthdays method to use the cached instance
        private async Task CheckBirthdays()
        {
            ResetBirthdaySentIfNewDay();

            var today = DateTime.Today;
            var birthdayPath = Path.Combine(AppContext.BaseDirectory, "birthdays.json");
            if (!File.Exists(birthdayPath))
            {
                var emptyData = new Dictionary<string, BirthdayCommand.BirthdayEntry>();
                File.WriteAllText(birthdayPath, JsonSerializer.Serialize(emptyData, CachedJsonSerializerOptions));
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, BirthdayCommand.BirthdayEntry>>(File.ReadAllText(birthdayPath), CachedJsonSerializerOptions);
            if (data == null || data.Count == 0) return;

            foreach (var (key, entry) in data)
            {
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

        public static void SaveGuildSettings()
        {
            SaveSettings();
        }

        public static GuildSettings GetSettings(ulong guildId)
        {
            if (!_guildSettings.ContainsKey(guildId))
                _guildSettings[guildId] = new GuildSettings();
            return _guildSettings[guildId];
        }

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

            // Parse: command + rest
            var content = userMessage.Content.Substring(argPos).Trim();
            string commandName, argRest = string.Empty;

            int firstSpace = content.IndexOf(' ');
            if (firstSpace < 0)
                commandName = content.ToLowerInvariant();
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

        // Update the ambiguous call to explicitly use the renamed method
        private async Task RepeatBirthdayCheck()
        {
            while (true)
            {
                var nextRunLocalMidnight = DateTime.Today.AddDays(1); // midnight next day
                var delay = nextRunLocalMidnight - DateTime.Now;
                if (delay < TimeSpan.FromSeconds(1))
                    delay = TimeSpan.FromSeconds(1);

                await Task.Delay(delay);
                await CheckBirthdaysInternal(); // Explicitly call the renamed method
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

        public void Dispose()
        {
            //_reminderService?.Dispose();
            ReminderService?.Dispose();// add more disposables if needed
        }
    }
}