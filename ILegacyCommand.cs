using Discord.WebSocket;

public interface ILegacyCommand
{
    string Name { get; }
    string Description { get; }
    string Category { get; }
    Task ExecuteAsync(SocketMessage message, string[] args);
}