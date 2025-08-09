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

namespace MyDiscordBot
{
    public class Bot
    {
        // --- Lifecycle/guards ---
        private volatile bool _didInit = false;
        private volatile bool _commandsLoaded = false;
        private volatile bool _birthdayLoopStarted = false;

        // prevent concurrent CheckBirthdays runs
        private readonly SemaphoreSlim _birthdayCheckGate = new(1, 1);

        // dedupe birthday posts per day
        private readonly HashSet<string> _birthdaySentToday = new();
        private DateTime _lastBirthdayResetDate = DateTime.MinValue;

        // --- Services ---
        private ReminderService _reminderService;
        public ReminderService ReminderService => _reminderService;

        // --- Discord client & config ---
        public DiscordSocketClient _client;
        private string _token;
        private string _prefix = "!";

        // --- Commands & settings ---
        private readonly Dictionary<string, ILegacyCommand> _legacyCommands = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<ulong, GuildSettings> _guildSettings = new();
        private const string SettingsFile = "guild-settings.json";

        public List<ILegacyCommand> GetAllLegacyCommands()
        {
            return _legacyCommands.Values.ToList();
        }

        public static Bot BotInstance { get; private set; }

        public DiscordSocketClient GetClient()
        {
            return _client;
        }

        public async Task RunAsync()
        {
            BotInstance = this;

            Env.Load();
            _token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
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
            Task ReadyHandler()
                => OnClientReadyOnce();

            _client.Ready += ReadyHandler;

            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();

            // keep alive
            await Task.Delay(Timeout.Infinite);
        }

        private async Task OnClientReadyOnce()
        {
            // If another Ready fired first, bail
            if (_didInit) return;

            // Try to claim init
            var claimed = false;
            lock (this)
            {
                if (!_didInit)
                {
                    _didInit = true;
                    claimed = true;
                }
            }
            if (!claimed) return;

            // Now we’ve claimed initialization — immediately unsubscribe to be extra safe
            _client.Ready -= OnClientReadyOnce;

            Console.WriteLine("[READY] Bot is online and Ready event was triggered.");

            LoadSettings();

            // Create ReminderService ONCE (ReminderService itself should have an internal loop guard)
            if (_reminderService == null)
                _reminderService = new ReminderService(_client);

            // Load commands ONCE globally (not per guild)
            if (!_commandsLoaded)
            {
                LoadLegacyCommandsOnce();
                _commandsLoaded = true;
            }

            // Per-guild nickname + hello log
            foreach (var guild in _client.Guilds)
            {
                var settings = GetSettings(guild.Id);
                if (!string.IsNullOrEmpty(settings.Nickname))
                {
                    var botUser = guild.GetUser(_client.CurrentUser.Id);
                    if (botUser != null)
                    {
                        try { await botUser.ModifyAsync(p => p.Nickname = settings.Nickname); }
                        catch { /* ignore perms errors */ }
                    }
                }

                LogMessage(guild.Id, $"Connected to guild: {guild.Name} ({guild.Id})", LogCategory.Log);
            }

            // Start birthday loop ONCE
            if (!_birthdayLoopStarted)
            {
                _birthdayLoopStarted = true;
                _ = RepeatBirthdayCheck();
            }

            // One immediate run (gated + deduped)
            await CheckBirthdays();
        }

        private void LoadLegacyCommandsOnce()
        {
            var commandTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ILegacyCommand).IsAssignableFrom(t)
                            && !t.IsAbstract && !t.IsInterface)
                .ToList();

            foreach (var type in commandTypes)
            {
                if (type.GetCustomAttribute<ObsoleteAttribute>() != null)
                    continue;

                var instance = (ILegacyCommand)Activator.CreateInstance(type);
                // last write wins, but names are case-insensitive via comparer
                _legacyCommands[instance.Name] = instance;
            }

            // optional log: per guild
            foreach (var g in _client.Guilds)
            {
                foreach (var kvp in _legacyCommands)
                    LogMessage(g.Id, $"Loaded legacy command: {_prefix}{kvp.Key}", LogCategory.Register);
            }
        }

        private void LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    _guildSettings = JsonSerializer.Deserialize<Dictionary<ulong, GuildSettings>>(File.ReadAllText(SettingsFile))
                                     ?? new Dictionary<ulong, GuildSettings>();
                }
                catch
                {
                    _guildSettings = new Dictionary<ulong, GuildSettings>();
                }
            }
            else
            {
                _guildSettings = new Dictionary<ulong, GuildSettings>();
            }
        }

        private void SaveSettings()
        {
            var json = JsonSerializer.Serialize(_guildSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
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

            // Use strings instead of Span<char> to stay C# 11 compatible
            var content = userMessage.Content.Substring(argPos).Trim();

            // Split once into command + rest
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

            var args = string.IsNullOrWhiteSpace(argRest)
                ? Array.Empty<string>()
                : argRest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (_legacyCommands.TryGetValue(commandName, out var command))
            {
                try
                {
                    await command.ExecuteAsync(message, args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Command '{commandName}' failed: {ex.Message}");
                }
            }
        }

        private async Task RepeatBirthdayCheck()
        {
            while (true)
            {
                var nextRunLocalMidnight = DateTime.Today.AddDays(1); // midnight next day
                var delay = nextRunLocalMidnight - DateTime.Now;
                if (delay < TimeSpan.FromSeconds(1))
                    delay = TimeSpan.FromSeconds(1);

                await Task.Delay(delay);
                await CheckBirthdays();
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

        private async Task CheckBirthdays()
        {
            await _birthdayCheckGate.WaitAsync();
            try
            {
                ResetBirthdaySentIfNewDay();

                var today = DateTime.Today;
                var birthdayPath = Path.Combine(AppContext.BaseDirectory, "birthdays.json");

                if (!File.Exists(birthdayPath))
                {
                    Console.WriteLine("[BirthdayCheck] No birthdays.json file found. Creating a new one...");
                    var emptyData = new Dictionary<string, BirthdayCommand.BirthdayEntry>();
                    var json = JsonSerializer.Serialize(emptyData, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(birthdayPath, json);
                    return;
                }

                var birthdayData = JsonSerializer.Deserialize<Dictionary<string, BirthdayCommand.BirthdayEntry>>(File.ReadAllText(birthdayPath));
                if (birthdayData == null || birthdayData.Count == 0)
                    return;

                foreach (var (key, entry) in birthdayData)
                {
                    var parts = key.Split('-');
                    if (parts.Length != 2
                        || !ulong.TryParse(parts[0], out var guildId)
                        || !ulong.TryParse(parts[1], out var userId))
                        continue;

                    var guild = _client.GetGuild(guildId);
                    if (guild == null) continue;

                    if (entry.Date.Month == today.Month && entry.Date.Day == today.Day)
                    {
                        var user = guild.GetUser(userId);
                        var channel = guild.TextChannels.FirstOrDefault(c =>
                            guild.CurrentUser.GetPermissions(c).SendMessages &&
                            (GetSettings(guildId).BirthdayChannelId == 0 || c.Id == GetSettings(guildId).BirthdayChannelId));

                        if (channel != null && user != null)
                        {
                            var sentKey = $"{guildId}:{userId}:{DateTime.UtcNow:yyyy-MM-dd}";
                            if (_birthdaySentToday.Contains(sentKey))
                            {
                                LogMessage(guildId, $"[BirthdayCheck] Skipping duplicate for {entry.Username}", LogCategory.BirthdayCheck);
                                continue;
                            }

                            LogMessage(guildId, $"🎉 Birthday match for {entry.Username}", LogCategory.BirthdayCheck);
                            await channel.SendMessageAsync($"🎉 Happy Birthday {user.Mention}!");
                            _birthdaySentToday.Add(sentKey);
                        }
                        else
                        {
                            LogMessage(guildId, $"No accessible birthday channel for {entry.Username}", LogCategory.BirthdayCheck);
                        }
                    }
                    else
                    {
                        LogMessage(guildId, $"❌ No birthday match today for {entry.Username} ({entry.Date:MM/dd})", LogCategory.BirthdayCheck);
                    }
                }
            }
            finally
            {
                _birthdayCheckGate.Release();
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
            BotInstance.SaveSettings();
        }

        public static bool ToggleLogCategory(ulong guildId, LogCategory category)
        {
            var settings = GetSettings(guildId);
            if (settings.LogCategories.Contains(category.ToString()))
                settings.LogCategories.Remove(category.ToString());
            else
                settings.LogCategories.Add(category.ToString());

            BotInstance.SaveSettings();
            return settings.LogCategories.Contains(category.ToString());
        }

        private void LogMessage(ulong guildId, string message, LogCategory category = LogCategory.Other)
        {
            if (IsLogCategoryEnabled(guildId, category))
                Console.WriteLine($"[{category}] {message}");
        }

        public void Shutdown()
        {
            _reminderService?.Dispose();
            // add more disposables if needed
        }
    }
}