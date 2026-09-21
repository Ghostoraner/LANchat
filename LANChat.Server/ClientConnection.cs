using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using LANChat.Server.Models;

namespace LANChat.Server
{
    public class ClientConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly ClientManager _clientManager;
        private readonly X509Certificate2 _certificate;
        private SslStream? _sslStream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        
        public string Username { get; private set; } = string.Empty;
        private DateTime _lastMessageTime = DateTime.MinValue;

       
        private const int MaxMessageLength = 5000; 
        private const int RateLimitMs = 500;

        public ClientConnection(TcpClient tcpClient, ClientManager clientManager, X509Certificate2 certificate)
        {
            _tcpClient = tcpClient;
            _clientManager = clientManager;
            _certificate = certificate;
        }

        public async Task ProcessAsync()
        {
            try
            {
                var stream = _tcpClient.GetStream();
                _sslStream = new SslStream(stream, false);
                await _sslStream.AuthenticateAsServerAsync(_certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, true);

                _reader = new StreamReader(_sslStream);
                _writer = new StreamWriter(_sslStream) { AutoFlush = true };

                string? line = await _reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line) || line.Length > MaxMessageLength) return;

                var initialMsg = JsonSerializer.Deserialize<ChatMessage>(line);
                string requestedName = initialMsg?.Sender?.Trim() ?? "User";
                if (string.IsNullOrEmpty(requestedName)) requestedName = "User";

                if (_clientManager.IsUsernameTaken(requestedName))
                {
                    await SendSystemErrorAsync("Этот ник уже занят. Выберите другой.");
                    return;
                }

                Username = requestedName;
                if (!_clientManager.TryAddClient(Username, this))
                {
                    await SendSystemErrorAsync("Не удалось зарегистрировать ник.");
                    return;
                }

                await _writer.WriteLineAsync(JsonSerializer.Serialize(new ChatMessage
                {
                    Sender = "Система", Type = "system", Content = "Успешно подключено к серверу (TLS).", Timestamp = DateTime.UtcNow
                }));

                BroadcastUserList();
                _clientManager.Broadcast(JsonSerializer.Serialize(new ChatMessage
                {
                    Sender = "Система", Type = "system", Content = $"{Username} присоединился к чату.", Timestamp = DateTime.UtcNow
                }), Username);

                while (_tcpClient.Connected)
                {
                    line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    
                    if (line.Length > MaxMessageLength) continue;

                   
                    if ((DateTime.UtcNow - _lastMessageTime).TotalMilliseconds < RateLimitMs)
                    {
                        await SendSystemErrorAsync("Слишком частые сообщения. Подождите.");
                        continue;
                    }
                    _lastMessageTime = DateTime.UtcNow;

                    var msg = JsonSerializer.Deserialize<ChatMessage>(line);
                    if (msg != null && msg.Type != "error" && msg.Type != "system")
                    {
                        msg.Sender = Username;
                        msg.Timestamp = DateTime.UtcNow;
                        _clientManager.Broadcast(JsonSerializer.Serialize(msg));
                    }
                }
            }
            catch {}
            finally
            {
                if (!string.IsNullOrEmpty(Username))
                {
                    _clientManager.Remove(Username);
                    BroadcastUserList();
                    _clientManager.Broadcast(JsonSerializer.Serialize(new ChatMessage
                    {
                        Sender = "Система", Type = "system", Content = $"{Username} покинул чат.", Timestamp = DateTime.UtcNow
                    }));
                }
                _tcpClient.Close();
            }
        }

        public async Task SendMessageAsync(string jsonMessage)
        {
            if (_writer == null) return;
            try { await _writer.WriteLineAsync(jsonMessage); }
            catch {  }
        }

        private async Task SendSystemErrorAsync(string error)
        {
            if (_writer == null) return;
            await _writer.WriteLineAsync(JsonSerializer.Serialize(new ChatMessage
            {
                Sender = "Система", Type = "error", Content = error, Timestamp = DateTime.UtcNow
            }));
        }

        private void BroadcastUserList()
        {
            var users = string.Join(",", _clientManager.GetOnlineUsernames());
            _clientManager.Broadcast(JsonSerializer.Serialize(new ChatMessage
            {
                Sender = "System", Type = "users", Content = users, Timestamp = DateTime.UtcNow
            }));
        }
    }
}