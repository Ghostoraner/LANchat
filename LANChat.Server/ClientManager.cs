using System.Collections.Concurrent;
using System.Threading.Tasks;
using LANChat.Server.Models;

namespace LANChat.Server
{
    public class ClientManager
    {
        private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();

        public void Add(ClientConnection client)
        {
            _clients[client.Id] = client;
        }

        public void Remove(ClientConnection client)
        {
            _clients.TryRemove(client.Id, out _);
        }

        public async Task BroadcastAsync(ChatMessage message, string? excludeClientId = null)
        {
            foreach (var client in _clients.Values)
            {
                if (client.Id != excludeClientId)
                {
                    await client.SendMessageAsync(message);
                }
            }
        }
    }
}