using System;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LANChat.Client.ViewModels;

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _sender = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private bool _isMe;
    
    public string TimeFormatted => Timestamp.ToLocalTime().ToString("HH:mm");
    public HorizontalAlignment Alignment => IsMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public string BubbleColor => IsMe ? "#2b5278" : "#444444";
}