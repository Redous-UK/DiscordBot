using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DotNetEnv;
using MyDiscordBot.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyDiscordBot
{
    public class Bot
    {
        // --- Init / services ---
        private bool _didInit = false;
        private ReminderService _reminderService;
        public ReminderService ReminderService => _reminderService;

        // --- Discord client & config ---
        private DiscordSocketClient _client;
        private string _token;
        private string _prefix;

        // --- Commands & settings ---
        private readonly Dictionary<string, ILegacyCommand> _legacyCommands = new();
        private static Dictionary<ulong, GuildSettings> _guildSettings = new();
        private const string SettingsFile = "guild-settings.json";

        // --- Birthday loop guards / dedupe ---
        private bool _birthdayLoopStarted = false;
        private readonly HashSet<string> _birthdaySentToday = new();
        private DateTime _lastBirthdayResetDate = DateTime.MinValue;

        public static Bot BotInstance { get; private set; }
        // public string Prefix => _prefix;
        // public List<ILegacyCommand> GetAllLegacyCommands() => _legacyCommands.Values.ToList();

        public async Task RunAsync()
        {
            BotInstance = this;
            Env.Load();
            _token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            _prefix = Environment.GetEnvironmentVariable("PREFIX") ?? "!";

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds |
                                 GatewayIntents.GuildMessages |
                                 GatewayIntents.MessageContent |
                                 GatewayIntents.GuildMembers |
                                 GatewayIntents.GuildMessageReactions |
                                 GatewayIntents.GuildVoiceStates |
                                 GatewayIntents.GuildPresences
            });

            _client.Log += Log;
            _client.MessageReceived += HandleMessageAsync;
            _client.UserJoined += HandleUserJoinedAsync;
            _client.UserLeft += HandleUserLeftAsync;
            _client.UserVoiceStateUpdated += HandleVoiceStateAsync;
            _client.PresenceUpdated += HandlePresenceUpdatedAsync;
            _client.ReactionAdded += HandleReactionAddedAsync;

            _client.Ready += async () =>
            {
                // Guard: run this block only once per process
                if (_didInit) return;
                _didInit = true;

                Console.WriteLine("[READY] Bot is online and Ready event was triggered.");

                LoadSettings();

                // Create ReminderService ONCE
                if (_reminderService == null)
                    _reminderService = new ReminderService(_client);

                // Load commands / set nickname per guild
                foreach (var guild in _client.Guilds)
                {
                    LoadLegacyCommandsForGuild(guild.Id);

                    var settings = GetSettings(guild.Id);
                    if (!string.IsNullOrEmpty(settings.Nickname))
                    {
                        var botUser = guild.GetUser(_client.CurrentUser.Id);
                        if (botUser != null)
                            await botUser.ModifyAsync(p => p.Nickname = settings.Nickname);
                    }

                    LogMessage(guild.Id, $"Connected to guild: {guild.Name} ({guild.Id})", LogCategory.Log);
                }

                // Start daily birthday loop ONCE
                if (!_birthdayLoopStarted)
                {
                    _birthdayLoopStarted = true;
                    _ = RepeatBirthdayCheck();
                }

                // Run an immediate birthday check (deduped below)
                await CheckBirthdays();
            };

            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private void LoadLegacyCommandsForGuild(ulong guildId)
        {
            var commandTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ILegacyCommand).IsAssignableFrom(t)
                            && !t.IsInterface)
                .ToList();

            foreach (var type in commandTypes)
            {
                if (type.GetCustomAttribute<ObsoleteAttribute>() != null)
                {
                    LogMessage(guildId, $"Skipped deprecated command: {type.Name}", LogCategory.Register);
                    continue;
                }

                var instance = (ILegacyCommand)Activator.CreateInstance(type);
                _legacyCommands[instance.Name.ToLower()] = instance;
                LogMessage(guildId, $"Loaded legacy command: {_prefix}{instance.Name.ToLower()}", LogCategory.Register);
            }
        }

        private void LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                _guildSettings = JsonSerializer.Deserialize<Dictionary<ulong, GuildSettings>>(File.ReadAllText(SettingsFile));
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
            // Ignore system or bot messages
            if (message.Author.IsBot || string.IsNullOrWhiteSpace(message.Content))
                return;

            if (message is not SocketUserMessage userMessage)
                return;

            int argPos = 0;
            if (!userMessage.HasStringPrefix(_prefix, ref argPos))
                return;

            // You create a context but you're not using Commands library here; keeping it is harmless
            var context = new SocketCommandContext(_client, userMessage);

            var commandName = userMessage.Content.Substring(_prefix.Length).Split(' ')[0].ToLower();
            var args = userMessage.Content.Substring(_prefix.Length + commandName.Length).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

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
                // Sleep until next midnight (local)
                var nextRun = DateTime.Today.AddDays(1); // midnight next day
                var delay = nextRun - DateTime.Now;
                if (delay < TimeSpan.FromSeconds(1))
                    delay = TimeSpan.FromSeconds(1);

                await Task.Delay(delay);
                await CheckBirthdays();
            }
        }

        public DiscordSocketClient GetClient()
        {
            return _client;
        }

        public void SaveGuildSettings()
        {
            SaveSettings();
        }

        // === Birthday dedupe helpers ===
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
                if (parts.Length != 2 || !ulong.TryParse(parts[0], out var guildId) || !ulong.TryParse(parts[1], out var userId))
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

        private Task HandleUserJoinedAsync(SocketGuildUser user)
        {
            if (IsLogCategoryEnabled(user.Guild.Id, LogCategory.Register))
            {
                Console.WriteLine($"[JOIN] {user.Username} joined {user.Guild.Name}");
            }
            return Task.CompletedTask;
        }

        private Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
        {
            if (IsLogCategoryEnabled(guild.Id, LogCategory.Register))
            {
                Console.WriteLine($"[LEAVE] {user.Username} left {guild.Name}");
            }
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
                {
                    Console.WriteLine($"[PRESENCE] {user.Username} went from {before.Status} to {after.Status}");
                }
            }
            return Task.CompletedTask;
        }

        private Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            if (reaction.User.IsSpecified && reaction.Channel is SocketGuildChannel guildChannel)
            {
                var guildId = guildChannel.Guild.Id;
                if (IsLogCategoryEnabled(guildId, LogCategory.Reaction))
                {
                    Console.WriteLine($"[REACTION] {reaction.UserId} reacted with {reaction.Emote.Name} in channel {channel.Id}");
                }
            }
            return Task.CompletedTask;
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine("[LOG] " + msg.ToString());
            return Task.CompletedTask;
        }

        public static bool IsLogCategoryEnabled(ulong guildId, LogCategory category)
        {
            return GetSettings(guildId).LogCategories.Contains(category.ToString());
        }

        public static bool GetDebugMode(ulong guildId)
        {
            return GetSettings(guildId).DebugEnabled;
        }

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
            // Add more disposables later if needed
        }
    }
}