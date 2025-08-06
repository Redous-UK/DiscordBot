using Discord.WebSocket;
using System.Threading.Tasks;

public interface ILegacyCommand
{
    string Name { get; }
    string Description { get; }
    string Category { get; }
    Task ExecuteAsync(SocketMessage message, string[] args);
}