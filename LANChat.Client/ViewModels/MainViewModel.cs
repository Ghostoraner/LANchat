using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LANChat.Client.Models;
using LANChat.Client.Services;

namespace LANChat.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string EveryoneOption = "Всем";
    private const int FileChunkChars = 4000;
    private static readonly TimeSpan TypingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TypingSendThrottle = TimeSpan.FromSeconds(2);

    private readonly ChatClient _chatClient;
    private readonly Dictionary<string, DispatcherTimer> _typingTimers = new();
    private readonly Dictionary<string, FileTransferState> _incomingFiles = new();
    private DateTime _lastTypingSentAt = DateTime.MinValue;

    [ObservableProperty] private string _ipAddress = "127.0.0.1";
    [ObservableProperty] private string _port = "5000";
    [ObservableProperty] private string _username = "User";
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectButtonText = "Подключиться";
    [ObservableProperty] private bool _isSearchingServer;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _selectedRecipient = EveryoneOption;
    [ObservableProperty] private string _typingStatusText = string.Empty;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
    public ObservableCollection<string> OnlineUsers { get; } = new();
    public ObservableCollection<string> RecipientOptions { get; } = new() { EveryoneOption };

    
    public Func<Task<(string Path, string Name, long Size)?>>? RequestFilePick { get; set; }

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
            ResetRecipientOptions();
            ClearAllTyping();
            _incomingFiles.Clear();
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

        string? to = SelectedRecipient == EveryoneOption ? null : SelectedRecipient;
        await _chatClient.SendMessageAsync(MessageText, to);
        MessageText = string.Empty;
    }

    
    public void NotifyTyping()
    {
        if (!IsConnected) return;
        if (DateTime.UtcNow - _lastTypingSentAt < TypingSendThrottle) return;
        _lastTypingSentAt = DateTime.UtcNow;
        _ = _chatClient.SendTypingAsync();
    }

    [RelayCommand]
    public async Task AttachFileAsync()
    {
        if (!IsConnected || RequestFilePick == null) return;

        var picked = await RequestFilePick();
        if (picked == null) return;
        var (path, name, size) = picked.Value;

        string? to = SelectedRecipient == EveryoneOption ? null : SelectedRecipient;
        string transferId = Guid.NewGuid().ToString("N");

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            string base64 = Convert.ToBase64String(bytes);
            int totalChunks = Math.Max(1, (int)Math.Ceiling(base64.Length / (double)FileChunkChars));

            for (int i = 0; i < totalChunks; i++)
            {
                string chunk = base64.Substring(i * FileChunkChars, Math.Min(FileChunkChars, base64.Length - i * FileChunkChars));
                await _chatClient.SendRawAsync(new ChatMessage
                {
                    Type = "file_chunk",
                    Content = chunk,
                    To = to,
                    TransferId = transferId,
                    FileName = name,
                    FileSize = size,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    Timestamp = DateTime.UtcNow
                });
            }

            
            Messages.Add(new ChatMessageViewModel
            {
                Sender = to != null ? $"Вы → {to}" : Username,
                Timestamp = DateTime.UtcNow,
                IsMe = true,
                IsPrivate = to != null,
                IsFile = true,
                FileName = name,
                FilePath = path,
                FileSizeLabel = FormatFileSize(size)
            });
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Ошибка отправки файла: {ex.Message}");
        }
    }

    private void OnMessageReceived(ChatMessage msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (msg.Type)
            {
                case "history":
                    HandleHistory(msg.Content);
                    return;

                case "typing":
                    if (!string.IsNullOrEmpty(msg.Sender) && msg.Sender != Username)
                        RegisterTyping(msg.Sender);
                    return;

                case "file_chunk":
                    HandleFileChunk(msg);
                    return;
            }

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
                RefreshRecipientOptions();
                return;
            }

            if (!string.IsNullOrEmpty(msg.Sender))
                ClearTyping(msg.Sender);

            bool isPrivate = !string.IsNullOrEmpty(msg.To);
            string senderLabel = isPrivate && msg.Sender == Username ? $"Вы → {msg.To}" : (msg.Sender ?? string.Empty);

            Messages.Add(new ChatMessageViewModel
            {
                Sender = senderLabel,
                Content = msg.Content,
                Timestamp = msg.Timestamp,
                IsMe = msg.Sender == Username,
                IsPrivate = isPrivate
            });
        });
    }

    private void HandleHistory(string content)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<ChatMessage>>(content);
            if (items == null) return;
            foreach (var item in items)
            {
                Messages.Add(new ChatMessageViewModel
                {
                    Sender = item.Sender,
                    Content = item.Content,
                    Timestamp = item.Timestamp,
                    IsMe = item.Sender == Username
                });
            }
        }
        catch {  }
    }

    private void HandleFileChunk(ChatMessage msg)
    {
        if (string.IsNullOrEmpty(msg.TransferId)) return;

        if (!_incomingFiles.TryGetValue(msg.TransferId, out var state))
        {
            int total = msg.TotalChunks ?? 1;
            state = new FileTransferState
            {
                FileName = msg.FileName ?? "файл",
                FileSize = msg.FileSize ?? 0,
                Sender = msg.Sender ?? "?",
                To = msg.To,
                Chunks = new string?[total]
            };
            _incomingFiles[msg.TransferId] = state;
        }

        int idx = msg.ChunkIndex ?? 0;
        if (idx >= 0 && idx < state.Chunks.Length)
            state.Chunks[idx] = msg.Content;

        if (state.Chunks.Any(c => c == null)) return;

        _incomingFiles.Remove(msg.TransferId);
        try
        {
            string base64 = string.Concat(state.Chunks);
            byte[] bytes = Convert.FromBase64String(base64);

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LANChat", "Received");
            Directory.CreateDirectory(dir);
            string savePath = GetUniqueFilePath(dir, state.FileName);
            File.WriteAllBytes(savePath, bytes);

            bool isPrivate = !string.IsNullOrEmpty(state.To);
            Messages.Add(new ChatMessageViewModel
            {
                Sender = state.Sender,
                Timestamp = DateTime.UtcNow,
                IsMe = false,
                IsPrivate = isPrivate,
                IsFile = true,
                FileName = state.FileName,
                FilePath = savePath,
                FileSizeLabel = FormatFileSize(state.FileSize)
            });
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Не удалось сохранить файл \"{state.FileName}\": {ex.Message}");
        }
    }

    private static string GetUniqueFilePath(string dir, string fileName)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;

        string name = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            i++;
        } while (File.Exists(candidate));
        return candidate;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} КБ";
        return $"{bytes / (1024.0 * 1024.0):F1} МБ";
    }

    private void RegisterTyping(string user)
    {
        if (_typingTimers.TryGetValue(user, out var existing))
            existing.Stop();

        var timer = new DispatcherTimer { Interval = TypingTimeout };
        timer.Tick += (_, _) => ClearTyping(user);
        timer.Start();
        _typingTimers[user] = timer;
        UpdateTypingStatusText();
    }

    private void ClearTyping(string user)
    {
        if (_typingTimers.Remove(user, out var timer))
        {
            timer.Stop();
            UpdateTypingStatusText();
        }
    }

    private void ClearAllTyping()
    {
        foreach (var timer in _typingTimers.Values) timer.Stop();
        _typingTimers.Clear();
        UpdateTypingStatusText();
    }

    private void UpdateTypingStatusText()
    {
        var names = _typingTimers.Keys.ToList();
        TypingStatusText = names.Count switch
        {
            0 => string.Empty,
            1 => $"{names[0]} печатает...",
            _ => $"{string.Join(", ", names)} печатают..."
        };
    }

    private void ResetRecipientOptions()
    {
        RecipientOptions.Clear();
        RecipientOptions.Add(EveryoneOption);
        SelectedRecipient = EveryoneOption;
    }

    private void RefreshRecipientOptions()
    {
        var prev = SelectedRecipient;
        RecipientOptions.Clear();
        RecipientOptions.Add(EveryoneOption);
        foreach (var user in OnlineUsers)
        {
            if (user != Username) RecipientOptions.Add(user);
        }
        SelectedRecipient = RecipientOptions.Contains(prev) ? prev : EveryoneOption;
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

    private class FileTransferState
    {
        public string FileName = "";
        public long FileSize;
        public string Sender = "";
        public string? To;
        public string?[] Chunks = Array.Empty<string?>();
    }
}