using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LANChat.Server.Models;

namespace LANChat.Server
{
    public class ChatServer
    {
        private readonly int _port;
        private TcpListener? _listener;
        private readonly ClientManager _clientManager = new();
        private CancellationTokenSource? _cts;

        public ChatServer(int port)
        {
            _port = port;
        }

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _cts = new CancellationTokenSource();

            Console.WriteLine($"[SERVER] Сервер запущен на порту {_port}.");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
                    var clientConnection = new ClientConnection(tcpClient);

                    _clientManager.Add(clientConnection);

                    clientConnection.OnMessageReceived += async (sender, msg) =>
                    {
                        if (msg.Type == "join")
                        {
                            sender.Username = string.IsNullOrWhiteSpace(msg.Sender) ? "Anonymous" : msg.Sender;
                            Console.WriteLine($"[SERVER] Пользователь представился как: {sender.Username}");
                            
                            await _clientManager.BroadcastAsync(new ChatMessage
                            {
                                Type = "system",
                                Sender = "Server",
                                Content = $"{sender.Username} присоединился к чату."
                            }, sender.Id);
                            return;
                        }

                        Console.WriteLine($"[{sender.Username}]: {msg.Content}");
                        msg.Sender = sender.Username;
                        await _clientManager.BroadcastAsync(msg, sender.Id);
                    };

                    clientConnection.OnDisconnected += async (sender) =>
                    {
                        _clientManager.Remove(sender);
                        Console.WriteLine($"[SERVER] Клиент {sender.Username} отключился.");
                        await _clientManager.BroadcastAsync(new ChatMessage
                        {
                            Type = "system",
                            Sender = "Server",
                            Content = $"{sender.Username} покинул чат."
                        });
                    };

                    clientConnection.StartListening();
                    Console.WriteLine($"[SERVER] Новое TCP-соединение (ID: {clientConnection.Id})");
                }
            }
            catch (OperationCanceledException)
            {
                
            }
            finally
            {
                _listener.Stop();
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }
    }
}