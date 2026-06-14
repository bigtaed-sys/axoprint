using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AxoPrint.Setup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.ConnectAsync();
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.AddSelectedAsync();
    }

    // Closing hides to tray so the uploader keeps forwarding print jobs.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
