using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LANChat.Server
{
    public class ChatServer
    {
        private readonly TcpListener _listener;
        private readonly ClientManager _clientManager;
        private bool _isRunning;

        public ChatServer(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _clientManager = new ClientManager();
        }

        public async Task StartAsync()
        {
            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"[SERVER] Сервер запущен на порту {((IPEndPoint)_listener.LocalEndpoint).Port}");

            try
            {
                while (_isRunning)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    Console.WriteLine($"[SERVER] Новое TCP-соединение установлено.");

                    var clientConnection = new ClientConnection(tcpClient, _clientManager);
                    _ = Task.Run(() => clientConnection.ProcessAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER] Ошибка сервера: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
        }
    }
}