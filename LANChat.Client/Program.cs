using System;
using Avalonia;

namespace LANChat.Client;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        
        Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTOR", "1.5");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}