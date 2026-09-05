using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LANChat.Client.Models;
using LANChat.Client.Services;

namespace LANChat.Client;

public partial class MainWindow : Window
{
    private ChatClient? _chatClient;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BtnConnect_Click(object? sender, RoutedEventArgs e)
    {
        if (_chatClient != null && _chatClient.IsConnected)
        {
            _chatClient.Disconnect();
            BtnConnect.Content = "Подключиться";
            LbChat.Items.Add("[СИСТЕМА] Вы отключились от сервера.");
            return;
        }

        string ip = TxtIp.Text?.Trim() ?? "127.0.0.1";
        if (!int.TryParse(TxtPort.Text, out int port)) port = 5000;
        string username = TxtUsername.Text?.Trim() ?? "User";

        _chatClient = new ChatClient();
        _chatClient.OnMessageReceived += MessageReceived;

        try
        {
            await _chatClient.ConnectAsync(ip, port, username);
            BtnConnect.Content = "Отключиться";
            LbChat.Items.Add($"[СИСТЕМА] Успешно подключено к {ip}:{port}");
        }
        catch (Exception ex)
        {
            LbChat.Items.Add($"[СИСТЕМА] Ошибка подключения: {ex.Message}");
        }
    }

    private async void BtnSend_Click(object? sender, RoutedEventArgs e)
    {
        if (_chatClient == null || !_chatClient.IsConnected)
        {
            LbChat.Items.Add("[СИСТЕМА] Сначала подключитесь к серверу!");
            return;
        }

        string msg = TxtMessage.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(msg))
        {
            await _chatClient.SendMessageAsync(msg);
            TxtMessage.Text = string.Empty;
        }
    }

    private void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnSend_Click(sender, e);
        }
    }

    private void MessageReceived(ChatMessage msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LbChat.Items.Add($"{msg.Sender}: {msg.Content}");
            if (LbChat.ItemCount > 0)
            {
                LbChat.ScrollIntoView(LbChat.ItemCount - 1);
            }
        });
    }
}