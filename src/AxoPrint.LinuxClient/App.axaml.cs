using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AxoPrint.LinuxClient.Services;

namespace AxoPrint.LinuxClient;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = LinuxConfig.Load();
            desktop.MainWindow = new MainWindow(new MainViewModel(config));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
