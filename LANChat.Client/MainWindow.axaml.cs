using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LANChat.Client.ViewModels;

namespace LANChat.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
       
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        viewModel.RequestFilePick = PickFileForSendAsync;
        TbMessageInput.TextChanged += (_, _) => viewModel.NotifyTyping();

        Loaded += (s, e) =>
        {
            viewModel.Messages.CollectionChanged += (sender, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null && args.NewItems.Count > 0)
                {
                    LbChat.ScrollIntoView(args.NewItems[^1]);
                }
            };
        };
    }

    private async Task<(string Path, string Name, long Size)?> PickFileForSendAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите файл для отправки",
            AllowMultiple = false
        });

        var file = files.FirstOrDefault();
        if (file == null) return null;

        string? localPath = file.TryGetLocalPath();
        if (localPath == null) return null;

        var props = await file.GetBasicPropertiesAsync();
        long size = (long)(props.Size ?? 0);

        return (localPath, file.Name, size);
    }
}