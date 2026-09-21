using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LANChat.Client.Models;
using LANChat.Client.Services;

namespace LANChat.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ChatClient _chatClient;

    [ObservableProperty] private string _ipAddress = "127.0.0.1";
    [ObservableProperty] private string _port = "5000";
    [ObservableProperty] private string _username = "User";
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectButtonText = "Подключиться";
    [ObservableProperty] private bool _isSearchingServer;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
    public ObservableCollection<string> OnlineUsers { get; } = new();

    public MainViewModel()
    {
        _chatClient = new ChatClient();
        _chatClient.OnMessageReceived += OnMessageReceived;

        
        _ = DiscoverServerAsync();
    }

    [RelayCommand]
    public async Task DiscoverServerAsync()
    {
        if (IsSearchingServer) return;

        IsSearchingServer = true;
        StatusMessage = "Поиск сервера в локальной сети...";

        try
        {
            string? foundIp = await _chatClient.DiscoverLocalServerAsync(timeoutMs: 3000);

            if (!string.IsNullOrEmpty(foundIp))
            {
                IpAddress = foundIp;
                StatusMessage = $"Сервер найден: {foundIp}";
                AddSystemMessage($"Автообнаружение: найден сервер {foundIp}");
            }
            else
            {
                StatusMessage = "Сервер не найден в LAN.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsSearchingServer = false;
        }
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            _chatClient.Disconnect();
            IsConnected = false;
            ConnectButtonText = "Подключиться";
            OnlineUsers.Clear();
            AddSystemMessage("Вы отключились от сервера.");
            return;
        }

        if (!int.TryParse(Port, out int portNum)) portNum = 5000;

        try
        {
            await _chatClient.ConnectAsync(IpAddress, portNum, Username);
            IsConnected = true;
            ConnectButtonText = "Отключиться";
            AddSystemMessage($"Успешно подключено к {IpAddress}:{portNum}");
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Ошибка подключения: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(MessageText)) return;

        await _chatClient.SendMessageAsync(MessageText);
        MessageText = string.Empty;
    }

    private void OnMessageReceived(ChatMessage msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            
            bool isUserListPacket = 
                (msg.Type != null && msg.Type.Equals("users", StringComparison.OrdinalIgnoreCase)) ||
                (msg.Sender?.Equals("System", StringComparison.OrdinalIgnoreCase) == true && msg.Content?.Contains(",") == true);

            if (isUserListPacket)
            {
                OnlineUsers.Clear();
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    foreach (var u in msg.Content.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = u.Trim();
                        if (!string.IsNullOrEmpty(name))
                            OnlineUsers.Add(name);
                    }
                }
                return; 
            }

           
            Messages.Add(new ChatMessageViewModel
            {
                Sender = msg.Sender,
                Content = msg.Content,
                Timestamp = msg.Timestamp,
                IsMe = msg.Sender == Username
            });
        });
    }

    private void AddSystemMessage(string text)
    {
        Messages.Add(new ChatMessageViewModel
        {
            Sender = "СИСТЕМА",
            Content = text,
            Timestamp = DateTime.UtcNow,
            IsMe = false
        });
    }
}