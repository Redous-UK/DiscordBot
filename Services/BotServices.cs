using System;

namespace MyDiscordBot.Services
{

    public sealed class BotServices : IDisposable
    {
        public ReminderService Reminders { get; }
        public GuildSettingsService GuildSettings { get; }

        public QnAApiService QnAApi { get; }

        public BotServices(ReminderService reminderService, QnAApiService qnaApiService)
        {
            Reminders = reminderService ?? throw new ArgumentNullException(nameof(reminderService));
            GuildSettings = new GuildSettingsService();

            QnAApi = qnaApiService ?? throw new ArgumentNullException(nameof(qnaApiService));
        }

        public void Dispose()
        {
            Reminders?.Dispose();
            QnAApi?.Dispose();
            // GuildSettingsService doesn't implement IDisposable in your code, so nothing to dispose here.
        }
    }
}