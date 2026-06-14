using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AxoPrint.Setup.Services;

namespace AxoPrint.Setup;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SetupConfig _config;

    public MainViewModel(SetupConfig config, Uploader uploader)
    {
        _config = config;
        _relayBaseUrl = config.RelayBaseUrl;
        _token = config.Token;
        uploader.Log += line => Dispatcher.UIThread.Post(() => AppendLog(line));
    }

    public ObservableCollection<PrinterRow> Printers { get; } = new();

    private string _relayBaseUrl;
    public string RelayBaseUrl { get => _relayBaseUrl; set => Set(ref _relayBaseUrl, value); }

    private string _token;
    public string Token { get => _token; set => Set(ref _token, value); }

    private string _status = "Enter the relay URL and token, then Connect.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _log = "";
    public string Log { get => _log; private set => Set(ref _log, value); }

    private bool _busy;
    public bool Busy { get => _busy; private set { Set(ref _busy, value); OnPropertyChanged(nameof(NotBusy)); } }
    public bool NotBusy => !_busy;

    private bool _runAtStartup = StartupManager.IsEnabled();
    public bool RunAtStartup
    {
        get => _runAtStartup;
        set
        {
            if (_runAtStartup == value) return;
            var (ok, output) = StartupManager.SetEnabled(value);
            if (ok)
            {
                _runAtStartup = value;
                Status = value ? "Autostart enabled." : "Autostart disabled.";
            }
            else
            {
                Status = "Autostart failed: " + output;
            }
            OnPropertyChanged(nameof(RunAtStartup));
        }
    }

    public async Task ConnectAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            _config.RelayBaseUrl = RelayBaseUrl.Trim();
            _config.Token = Token.Trim();
            _config.Save();

            Status = "Connecting…";
            var api = new RelayApi(_config);
            var items = await api.GetPrintersAsync(CancellationToken.None);
            var installed = WindowsInstaller.InstalledPrinterNames();

            Printers.Clear();
            foreach (var p in items)
            {
                var opt = _config.OptionsFor(p.QueueId);
                var row = new PrinterRow
                {
                    QueueId = p.QueueId,
                    DisplayName = p.DisplayName,
                    Url = p.Url,
                    Installed = installed.Contains(WindowsInstaller.PrinterName(p.DisplayName)),
                    AgentOnline = p.AgentOnline,
                    Duplex = opt.Duplex,
                    Monochrome = opt.Monochrome,
                };
                row.PropertyChanged += OnRowOptionChanged;
                Printers.Add(row);
            }

            Status = items.Count == 0
                ? "Connected, but the agent has not registered any printers yet."
                : $"Found {items.Count} printer(s)." + (items.Any(i => i.AgentOnline) ? "" : " (agent offline)");
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task AddSelectedAsync()
    {
        if (Busy) return;
        var chosen = Printers.Where(p => p.IsSelected).ToList();
        if (chosen.Count == 0)
        {
            Status = "Select at least one printer to add.";
            return;
        }

        Busy = true;
        try
        {
            int ok = 0;
            foreach (var row in chosen)
            {
                Status = $"Adding \"{row.DisplayName}\"…";
                string portFile = SetupConfig.PortFileFor(row.QueueId);
                var (success, output) = await WindowsInstaller.AddLocalPrinterAsync(
                    row.DisplayName, portFile, CancellationToken.None);
                if (success)
                {
                    row.Installed = true;
                    ok++;
                    AppendLog($"Installed printer \"AxoPrint: {row.DisplayName}\".");
                }
                else
                {
                    Status = $"Failed to add \"{row.DisplayName}\": {output}";
                    return;
                }
            }
            Status = $"Added {ok} printer(s). Print to \"AxoPrint: …\" from any app; keep this app running.";
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void OnRowOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PrinterRow row) return;
        if (e.PropertyName is nameof(PrinterRow.Duplex) or nameof(PrinterRow.Monochrome))
            _config.SetOptions(row.QueueId, new PrinterOptions { Duplex = row.Duplex, Monochrome = row.Monochrome });
    }

    private void AppendLog(string line)
    {
        string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        Log = string.IsNullOrEmpty(Log) ? stamped : stamped + "\n" + Log;
        if (Log.Length > 16000) Log = Log[..16000];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name!);
    }
}
