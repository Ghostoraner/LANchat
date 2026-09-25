using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LANChat.Client.Models;

namespace LANChat.Client.Services;

public class ChatClient
{
    private TcpClient? _tcpClient;
    private SslStream? _sslStream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _isRunning;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Username { get; private set; } = string.Empty;

    public event Action<ChatMessage>? OnMessageReceived;

   
    public async Task<string?> DiscoverLocalServerAsync(int timeoutMs = 3000)
    {
        using var udp = new UdpClient();
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5001));
        
        var timeoutTask = Task.Delay(timeoutMs);
        var receiveTask = udp.ReceiveAsync();

        var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
        if (completedTask == receiveTask)
        {
            var result = await receiveTask;
            string msg = Encoding.UTF8.GetString(result.Buffer);
            if (msg == "LANCHAT_SERVER_5000")
            {
                return result.RemoteEndPoint.Address.ToString();
            }
        }
        return null; 
    }

    public async Task ConnectAsync(string ip, int port, string username)
    {
        _tcpClient = new TcpClient();
        
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        await _tcpClient.ConnectAsync(ip, port);
        
        var stream = _tcpClient.GetStream();
        
        
        _sslStream = new SslStream(stream, false, (sender, cert, chain, errors) => true);
        await _sslStream.AuthenticateAsClientAsync("LANChatServer");

        _reader = new StreamReader(_sslStream);
        _writer = new StreamWriter(_sslStream) { AutoFlush = true };
        _isRunning = true;

        var handshake = new ChatMessage { Sender = username, Content = "", Timestamp = DateTime.UtcNow };
        await _writer.WriteLineAsync(JsonSerializer.Serialize(handshake));
        Username = username;

        string? line = await _reader.ReadLineAsync();
        if (line != null)
        {
            var response = JsonSerializer.Deserialize<ChatMessage>(line);
            if (response?.Type == "error")
            {
                _tcpClient.Close();
                throw new Exception(response.Content);
            }
            if (response != null) OnMessageReceived?.Invoke(response);
        }

        _ = Task.Run(ReceiveLoop);
    }

    public Task SendMessageAsync(string text, string? to = null)
    {
        var message = new ChatMessage { Type = "message", Content = text, Timestamp = DateTime.UtcNow, To = to };
        return SendRawAsync(message);
    }

    public Task SendTypingAsync()
    {
        var message = new ChatMessage { Type = "typing", Content = "", Timestamp = DateTime.UtcNow };
        return SendRawAsync(message);
    }

    
    public async Task SendRawAsync(ChatMessage message)
    {
        if (_writer == null || _tcpClient == null || !_tcpClient.Connected) return;
        string json = JsonSerializer.Serialize(message);

        
        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(json);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Disconnect()
    {
        _isRunning = false;
        _reader?.Close();
        _writer?.Close();
        _sslStream?.Close();
        _tcpClient?.Close();
    }

    private async Task ReceiveLoop()
    {
        try
        {
            while (_isRunning && _tcpClient!.Connected && _reader != null)
            {
                string? line = await _reader.ReadLineAsync();
                if (line == null) break;

                var message = JsonSerializer.Deserialize<ChatMessage>(line);
                if (message != null)
                {
                    OnMessageReceived?.Invoke(message);
                }
            }
        }
        catch { }
    }
}
