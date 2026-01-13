using System;

namespace MyDiscordBot.Services
{

    public sealed class BotServices : IDisposable
    {
        public ReminderService Reminders { get; }
        public GuildSettingsService GuildSettings { get; }

        public OpenTdbService TriviaApi { get; }

        public BotServices(ReminderService reminderService, OpenTdbService triviaApi)
        {
            Reminders = reminderService ?? throw new ArgumentNullException(nameof(reminderService));
            GuildSettings = new GuildSettingsService();
            TriviaApi = triviaApi ?? throw new ArgumentNullException(nameof(triviaApi));
        }

        public void Dispose()
        {
            Reminders?.Dispose();
            TriviaApi?.Dispose();
            // GuildSettingsService doesn't implement IDisposable in your code, so nothing to dispose here.
        }
    }
}