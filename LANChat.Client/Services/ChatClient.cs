using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using LANChat.Client.Models;

namespace LANChat.Client.Services;

public class ChatClient
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string _username = "User";

    public bool IsConnected => _client?.Connected ?? false;
    public event Action<ChatMessage>? OnMessageReceived;

    public async Task ConnectAsync(string ip, int port, string username)
    {
        _username = username;
        _client = new TcpClient();
        await _client.ConnectAsync(ip, port);
        
        var stream = _client.GetStream();
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };

        
        var joinMsg = new ChatMessage { Type = "join", Sender = _username, Content = "присоединился к чату" };
        await _writer.WriteLineAsync(JsonSerializer.Serialize(joinMsg));

        _ = ListenAsync();
    }

    public async Task SendMessageAsync(string text)
    {
        if (_writer != null && IsConnected)
        {
            var msg = new ChatMessage { Type = "message", Sender = _username, Content = text };
            await _writer.WriteLineAsync(JsonSerializer.Serialize(msg));
        }
    }

    private async Task ListenAsync()
    {
        try
        {
            while (IsConnected && _reader != null)
            {
                string? line = await _reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var msg = JsonSerializer.Deserialize<ChatMessage>(line);
                if (msg != null)
                {
                    OnMessageReceived?.Invoke(msg);
                }
            }
        }
        catch { }
        finally
        {
            Disconnect();
        }
    }

    public void Disconnect()
    {
        _client?.Close();
        _client = null;
    }
}