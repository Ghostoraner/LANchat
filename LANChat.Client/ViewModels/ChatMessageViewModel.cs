using System;
using System.Diagnostics;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LANChat.Client.ViewModels;

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _sender = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private bool _isMe;
    [ObservableProperty] private bool _isPrivate;

    // Файловое сообщение
    [ObservableProperty] private bool _isFile;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _fileSizeLabel = string.Empty;

    public string TimeFormatted => Timestamp.ToLocalTime().ToString("HH:mm");
    public HorizontalAlignment Alignment => IsMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public string BubbleColor => IsPrivate ? "#5b3a78" : (IsMe ? "#2b5278" : "#444444");

    [RelayCommand]
    private void OpenFile()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
        }
        catch { /* не удалось открыть — не критично */ }
    }
}