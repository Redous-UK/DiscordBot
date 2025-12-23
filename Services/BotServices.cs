using System;

namespace MyDiscordBot.Services
{
    /// <summary>
    /// Central place for all long-lived bot services.
    /// Construct once in Bot, reuse everywhere.
    /// </summary>
    public sealed class BotServices : IDisposable
    {
        public ReminderService Reminders { get; }
        public GuildSettingsService GuildSettings { get; }

        public BotServices(ReminderService reminderService)
        {
            Reminders = reminderService ?? throw new ArgumentNullException(nameof(reminderService));
            GuildSettings = new GuildSettingsService();
        }

        public void Dispose()
        {
            Reminders?.Dispose();
            // GuildSettingsService doesn't implement IDisposable in your code, so nothing to dispose here.
        }
    }
}