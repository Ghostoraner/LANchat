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

       
        private const int MaxMessageLength = 8192; 
        private const int RateLimitMs = 500;
        private const int MaxUsernameLength = 32;
        private const int MaxFileChunks = 5000; // ~5000 * ~4KB ≈ 20MB предел на файл
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

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
                await _sslStream.AuthenticateAsServerAsync(_certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, true)
                    .WaitAsync(HandshakeTimeout);

                _reader = new StreamReader(_sslStream);
                _writer = new StreamWriter(_sslStream) { AutoFlush = true };

                string? line = await _reader.ReadLineAsync().WaitAsync(HandshakeTimeout);
                if (string.IsNullOrEmpty(line) || line.Length > MaxMessageLength) return;

                var initialMsg = JsonSerializer.Deserialize<ChatMessage>(line);
                string requestedName = initialMsg?.Sender?.Trim() ?? "User";
                if (string.IsNullOrEmpty(requestedName)) requestedName = "User";

                // Санитизация ника: запрещаем запятую (ломает CSV-формат списка
                // онлайн-пользователей) и ограничиваем длину.
                requestedName = requestedName.Replace(",", "").Trim();
                if (string.IsNullOrEmpty(requestedName)) requestedName = "User";
                if (requestedName.Length > MaxUsernameLength)
                    requestedName = requestedName.Substring(0, MaxUsernameLength);

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
                })).WaitAsync(WriteTimeout);

                BroadcastUserList();
                _clientManager.Broadcast(JsonSerializer.Serialize(new ChatMessage
                {
                    Sender = "Система", Type = "system", Content = $"{Username} присоединился к чату.", Timestamp = DateTime.UtcNow
                }), Username);

                // Отдаём новому клиенту последние сообщения общего чата.
                var history = _clientManager.GetHistorySnapshot();
                if (history.Count > 0)
                {
                    string historyPayload = "[" + string.Join(",", history) + "]";
                    await SendMessageAsync(JsonSerializer.Serialize(new ChatMessage
                    {
                        Sender = "Система", Type = "history", Content = historyPayload, Timestamp = DateTime.UtcNow
                    }));
                }

                while (_tcpClient.Connected)
                {
                    // Тайм-аут защищает от "зависших" соединений: если клиент
                    // подключился и молчит дольше ReadTimeout, считаем его мёртвым.
                    line = await _reader.ReadLineAsync().WaitAsync(ReadTimeout);
                    if (line == null) break;

                    
                    if (line.Length > MaxMessageLength) continue;

                    ChatMessage? msg;
                    try { msg = JsonSerializer.Deserialize<ChatMessage>(line); }
                    catch { continue; }
                    if (msg == null || msg.Type == "error" || msg.Type == "system") continue;

                    // "Печатает..." и чанки файлов — служебный, частый трафик,
                    // на них не распространяется общий rate limit сообщений.
                    bool isThrottleExempt = msg.Type == "typing" || msg.Type == "file_chunk";
                    if (!isThrottleExempt)
                    {
                        if ((DateTime.UtcNow - _lastMessageTime).TotalMilliseconds < RateLimitMs)
                        {
                            await SendSystemErrorAsync("Слишком частые сообщения. Подождите.");
                            continue;
                        }
                        _lastMessageTime = DateTime.UtcNow;
                    }

                    // Подделка отправителя невозможна: сервер всегда переписывает Sender.
                    msg.Sender = Username;
                    msg.Timestamp = DateTime.UtcNow;
                    string outJson;

                    switch (msg.Type)
                    {
                        case "typing":
                            _clientManager.Broadcast(JsonSerializer.Serialize(msg), Username);
                            break;

                        case "file_chunk":
                            if (msg.TotalChunks is > MaxFileChunks or <= 0) continue;
                            outJson = JsonSerializer.Serialize(msg);
                            if (!string.IsNullOrEmpty(msg.To))
                                _clientManager.TrySendToUser(msg.To, outJson);
                            else
                                _clientManager.Broadcast(outJson, Username);
                            break;

                        case "message":
                            outJson = JsonSerializer.Serialize(msg);
                            if (!string.IsNullOrEmpty(msg.To))
                            {
                                // Личное сообщение: доставляем получателю и эхом — себе.
                                _clientManager.TrySendToUser(msg.To, outJson);
                                await SendMessageAsync(outJson);
                            }
                            else
                            {
                                _clientManager.Broadcast(outJson);
                                _clientManager.AddToHistory(outJson);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER] Соединение с '{Username}' закрыто: {ex.GetType().Name}: {ex.Message}");
            }
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
            try { await _writer.WriteLineAsync(jsonMessage).WaitAsync(WriteTimeout); }
            catch {  }
        }

        private async Task SendSystemErrorAsync(string error)
        {
            if (_writer == null) return;
            await _writer.WriteLineAsync(JsonSerializer.Serialize(new ChatMessage
            {
                Sender = "Система", Type = "error", Content = error, Timestamp = DateTime.UtcNow
            })).WaitAsync(WriteTimeout);
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