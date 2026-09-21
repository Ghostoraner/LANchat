using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace LANChat.Server
{
    public class ChatServer
    {
        private readonly TcpListener _listener;
        private readonly ClientManager _clientManager;
        private bool _isRunning;
        private readonly X509Certificate2 _sslCertificate;

        public ChatServer(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _clientManager = new ClientManager();
            _sslCertificate = GenerateSelfSignedCertificate();
        }

        public async Task StartAsync()
        {
            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"[SERVER] Сервер запущен на порту {((IPEndPoint)_listener.LocalEndpoint).Port}. TLS включен.");

            // Запускаем фоновый процесс обнаружения в LAN
            _ = Task.Run(StartDiscoveryBroadcaster);

            try
            {
                while (_isRunning)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    
                   
                    tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    Console.WriteLine($"[SERVER] Новое TCP-соединение установлено.");
                    var clientConnection = new ClientConnection(tcpClient, _clientManager, _sslCertificate);
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

        private async Task StartDiscoveryBroadcaster()
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            var endPoint = new IPEndPoint(IPAddress.Broadcast, 5001);
            var message = Encoding.UTF8.GetBytes("LANCHAT_SERVER_5000");

            while (_isRunning)
            {
                try
                {
                    await udp.SendAsync(message, message.Length, endPoint);
                    await Task.Delay(2000); 
                }
                catch {  }
            }
        }

        private X509Certificate2 GenerateSelfSignedCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("cn=LANChatServer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
        }
    }
}