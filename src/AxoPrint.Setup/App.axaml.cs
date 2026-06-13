using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AxoPrint.Setup.Services;

namespace AxoPrint.Setup;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = SetupConfig.Load();
            desktop.MainWindow = new MainWindow(new MainViewModel(config));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
