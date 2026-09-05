using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LANChat.Server.Models;

namespace LANChat.Server
{
    public class ClientConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly CancellationTokenSource _cts;

        public string Id { get; } = Guid.NewGuid().ToString();
        public string Username { get; set; } = "Anonymous";

        public event Func<ClientConnection, ChatMessage, Task>? OnMessageReceived;
        public event Action<ClientConnection>? OnDisconnected;

        public ClientConnection(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            var stream = tcpClient.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _cts = new CancellationTokenSource();
        }

        public void StartListening()
        {
            _ = ListenAsync(_cts.Token);
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string? rawMessage = await _reader.ReadLineAsync();
                    if (rawMessage == null) break; 

                    if (string.IsNullOrWhiteSpace(rawMessage)) continue;

                    try
                    {
                        var chatMessage = JsonSerializer.Deserialize<ChatMessage>(rawMessage);
                        if (chatMessage != null && OnMessageReceived != null)
                        {
                            await OnMessageReceived.Invoke(this, chatMessage);
                        }
                    }
                    catch (JsonException)
                    {
                       
                        await SendErrorAsync("Некорректный формат данных.");
                    }
                }
            }
            catch
            {
                
            }
            finally
            {
                Disconnect();
            }
        }

        public async Task SendMessageAsync(ChatMessage message)
        {
            try
            {
                string json = JsonSerializer.Serialize(message);
                await _writer.WriteLineAsync(json);
            }
            catch
            {
                Disconnect();
            }
        }

        public async Task SendErrorAsync(string errorText)
        {
            var errorMsg = new ChatMessage
            {
                Type = "system",
                Sender = "Server",
                Content = errorText
            };
            await SendMessageAsync(errorMsg);
        }

        public void Disconnect()
        {
            _cts.Cancel();
            _reader.Dispose();
            _writer.Dispose();
            _tcpClient.Close();
            OnDisconnected?.Invoke(this);
        }
    }
}