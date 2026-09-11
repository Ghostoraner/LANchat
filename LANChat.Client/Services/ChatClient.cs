using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using LANChat.Client.Models;

namespace LANChat.Client.Services;

public class ChatClient
{
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _isRunning;

    public event Action<ChatMessage>? OnMessageReceived;

    public async Task ConnectAsync(string ip, int port, string username)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(ip, port);
        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _isRunning = true;

        var handshake = new ChatMessage
        {
            Sender = username,
            Content = "",
            Timestamp = DateTime.UtcNow
        };
        await _writer.WriteLineAsync(JsonSerializer.Serialize(handshake));

        string? line = await _reader.ReadLineAsync();
        if (line != null)
        {
            var response = JsonSerializer.Deserialize<ChatMessage>(line);
            
            if (response?.Type == "error")
            {
                _tcpClient.Close();
                throw new Exception(response.Content);
            }

            if (response != null)
            {
                OnMessageReceived?.Invoke(response);
            }
        }

        _ = Task.Run(ReceiveLoop);
    }

    public async Task SendMessageAsync(string text)
    {
        if (_writer == null || !_tcpClient!.Connected) return;

        var message = new ChatMessage
        {
            Content = text,
            Timestamp = DateTime.UtcNow
        };

        await _writer.WriteLineAsync(JsonSerializer.Serialize(message));
    }

    public void Disconnect()
    {
        _isRunning = false;
        _reader?.Close();
        _writer?.Close();
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
        catch
        {
            
        }
    }
}