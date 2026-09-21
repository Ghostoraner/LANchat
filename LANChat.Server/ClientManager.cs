using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LANChat.Server
{
    public class ClientManager
    {
        private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.OrdinalIgnoreCase);

        public bool IsUsernameTaken(string username) => _clients.ContainsKey(username);

        public bool TryAddClient(string username, ClientConnection client) => _clients.TryAdd(username, client);

        public void Remove(string username)
        {
            if (!string.IsNullOrEmpty(username))
                _clients.TryRemove(username, out _);
        }

        public void Broadcast(string message, string excludeUser = "")
        {
            foreach (var kvp in _clients)
            {
                if (!kvp.Key.Equals(excludeUser, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        
                        _ = kvp.Value.SendMessageAsync(message);
                    }
                    catch
                    {
                       
                    }
                }
            }
        }

        public IEnumerable<string> GetOnlineUsernames() => _clients.Keys.ToList();
    }
}