using Discord.WebSocket;
using System.Threading.Tasks;

namespace MyDiscordBot
{
    public interface ILegacyCommand
    {
        string Name { get; }

        string Description { get; }

        Task ExecuteAsync(SocketMessage message, string[] args);
    }
}
