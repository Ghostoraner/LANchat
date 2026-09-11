using Avalonia.Controls;
using System.Collections.Specialized;
using LANChat.Client.ViewModels;

namespace LANChat.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
       
        var viewModel = new MainViewModel();
        DataContext = viewModel;

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
}