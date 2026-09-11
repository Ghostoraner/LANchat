using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using LANChat.Server.Models;

namespace LANChat.Server
{
    public class ClientConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly ClientManager _clientManager;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        public string Username { get; private set; } = string.Empty;

        public ClientConnection(TcpClient tcpClient, ClientManager clientManager)
        {
            _tcpClient = tcpClient;
            _clientManager = clientManager;
        }

        public async Task ProcessAsync()
        {
            var stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            try
            {
                string? line = await _reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) return;

                var initialMsg = JsonSerializer.Deserialize<ChatMessage>(line);
                string requestedName = initialMsg?.Sender?.Trim() ?? "User";

                if (string.IsNullOrEmpty(requestedName))
                {
                    requestedName = "User";
                }

                if (_clientManager.IsUsernameTaken(requestedName))
                {
                    var errorMsg = JsonSerializer.Serialize(new ChatMessage
                    {
                        Sender = "Система",
                        Type = "error",
                        Content = "Этот ник уже занят. Выберите другой.",
                        Timestamp = DateTime.UtcNow
                    });
                    await _writer.WriteLineAsync(errorMsg);
                    return;
                }

                Username = requestedName;
                if (!_clientManager.TryAddClient(Username, this))
                {
                    var errorMsg = JsonSerializer.Serialize(new ChatMessage
                    {
                        Sender = "Система",
                        Type = "error",
                        Content = "Не удалось зарегистрировать ник.",
                        Timestamp = DateTime.UtcNow
                    });
                    await _writer.WriteLineAsync(errorMsg);
                    return;
                }

                var okMsg = JsonSerializer.Serialize(new ChatMessage
                {
                    Sender = "Система",
                    Type = "system",
                    Content = "Успешно подключено к серверу.",
                    Timestamp = DateTime.UtcNow
                });
                await _writer.WriteLineAsync(okMsg);

                BroadcastUserList();
                _clientManager.Broadcast(JsonSerializer.Serialize(new ChatMessage
                {
                    Sender = "Система",
                    Type = "system",
                    Content = $"{Username} присоединился к чату.",
                    Timestamp = DateTime.UtcNow
                }), Username);

                while (_tcpClient.Connected)
                {
                    line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    var msg = JsonSerializer.Deserialize<ChatMessage>(line);
                    if (msg != null)
                    {
                        msg.Sender = Username;
                        msg.Timestamp = DateTime.UtcNow;
                        _clientManager.Broadcast(JsonSerializer.Serialize(msg));
                    }
                }
            }
            catch
            {
               
            }
            finally
            {
                if (!string.IsNullOrEmpty(Username))
                {
                    _clientManager.Remove(Username);
                    BroadcastUserList();
                    _clientManager.Broadcast(JsonSerializer.Serialize(new ChatMessage
                    {
                        Sender = "Система",
                        Type = "system",
                        Content = $"{Username} покинул чат.",
                        Timestamp = DateTime.UtcNow
                    }));
                }
                _tcpClient.Close();
            }
        }

        public async Task SendMessageAsync(string jsonMessage)
        {
            if (_writer == null) return;
            try
            {
                await _writer.WriteLineAsync(jsonMessage);
            }
            catch
            {
               
            }
        }

        private void BroadcastUserList()
        {
            var users = string.Join(",", _clientManager.GetOnlineUsernames());
            var userListMsg = JsonSerializer.Serialize(new ChatMessage
            {
                Sender = "System",
                Type = "users",
                Content = users,
                Timestamp = DateTime.UtcNow
            });
            _clientManager.Broadcast(userListMsg);
        }
    }
}