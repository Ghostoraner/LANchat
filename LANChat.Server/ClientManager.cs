using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LANChat.Server
{
    public class ClientManager
    {
        // Словарь с регистронезависимыми ключами (никнеймы не будут дублироваться и будут нормально удаляться)
        private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.OrdinalIgnoreCase);

        public bool IsUsernameTaken(string username)
        {
            return _clients.ContainsKey(username);
        }

        public bool TryAddClient(string username, ClientConnection client)
        {
            return _clients.TryAdd(username, client);
        }

        public void Remove(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                _clients.TryRemove(username, out _);
            }
        }

        public void Broadcast(string message, string excludeUser = "")
        {
            foreach (var kvp in _clients)
            {
                if (!kvp.Key.Equals(excludeUser, StringComparison.OrdinalIgnoreCase))
                {
                    _ = kvp.Value.SendMessageAsync(message);
                }
            }
        }

        public IEnumerable<string> GetOnlineUsernames()
        {
            return _clients.Keys.ToList();
        }
    }
}