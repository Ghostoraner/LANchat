using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LANChat.Server
{
    public class ClientManager
    {
        private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.OrdinalIgnoreCase);

        private const int MaxHistorySize = 50;
        private readonly object _historyLock = new();
        private readonly LinkedList<string> _history = new();

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

        /// <summary>Адресная отправка одному пользователю (для ЛС и файлов). true, если получатель онлайн.</summary>
        public bool TrySendToUser(string username, string message)
        {
            if (_clients.TryGetValue(username, out var client))
            {
                _ = client.SendMessageAsync(message);
                return true;
            }
            return false;
        }

        /// <summary>Добавляет сообщение в общую историю (только для публичных сообщений).</summary>
        public void AddToHistory(string jsonMessage)
        {
            lock (_historyLock)
            {
                _history.AddLast(jsonMessage);
                while (_history.Count > MaxHistorySize)
                    _history.RemoveFirst();
            }
        }

        public List<string> GetHistorySnapshot()
        {
            lock (_historyLock)
            {
                return _history.ToList();
            }
        }

        public IEnumerable<string> GetOnlineUsernames() => _clients.Keys.ToList();
    }
}