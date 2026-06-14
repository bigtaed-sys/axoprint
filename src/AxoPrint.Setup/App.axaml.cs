using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using AxoPrint.Setup.Services;

namespace AxoPrint.Setup;

public partial class App : Application
{
    private readonly CancellationTokenSource _cts = new();
    private MainWindow? _window;

    public static bool IsExiting { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var config = SetupConfig.Load();
            var uploader = new Uploader(config);
            var vm = new MainViewModel(config, uploader);

            _window = new MainWindow(vm);
            desktop.MainWindow = _window;
            _window.Show();

            SetupTrayIcon(desktop);

            // The uploader must keep running to forward spooled PDFs to the relay.
            _ = Task.Run(() => uploader.RunAsync(_cts.Token));
            desktop.ShutdownRequested += (_, _) => _cts.Cancel();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowIcon icon;
        try
        {
            icon = new WindowIcon(AssetLoader.Open(new Uri("avares://AxoPrint.Setup/Assets/icon.png")));
            _window!.Icon = icon;
        }
        catch { return; }

        var open = new NativeMenuItem("Open AxoPrint");
        open.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => { IsExiting = true; _cts.Cancel(); desktop.Shutdown(); };

        var menu = new NativeMenu();
        menu.Add(open);
        menu.Add(quit);

        var tray = new TrayIcon { Icon = icon, ToolTipText = "AxoPrint", IsVisible = true, Menu = menu };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { tray });
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
