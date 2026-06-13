using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.Setup.Services;

namespace AxoPrint.Setup;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SetupConfig _config;

    public MainViewModel(SetupConfig config)
    {
        _config = config;
        _relayBaseUrl = config.RelayBaseUrl;
        _token = config.Token;
    }

    public ObservableCollection<PrinterRow> Printers { get; } = new();

    private string _relayBaseUrl;
    public string RelayBaseUrl { get => _relayBaseUrl; set => Set(ref _relayBaseUrl, value); }

    private string _token;
    public string Token { get => _token; set => Set(ref _token, value); }

    private string _status = "Enter the relay URL and token, then Connect.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private bool _busy;
    public bool Busy { get => _busy; private set { Set(ref _busy, value); OnPropertyChanged(nameof(NotBusy)); } }
    public bool NotBusy => !_busy;

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
                Printers.Add(new PrinterRow
                {
                    QueueId = p.QueueId,
                    DisplayName = p.DisplayName,
                    Url = p.Url,
                    Installed = installed.Contains(p.DisplayName),
                    AgentOnline = p.AgentOnline,
                });
            }

            Status = items.Count == 0
                ? "Connected, but the agent has not registered any printers yet."
                : $"Found {items.Count} printer(s)." +
                  (items.Any(i => i.AgentOnline) ? "" : " (agent currently offline)");
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
                var (success, output) = await WindowsInstaller.AddPrinterAsync(
                    row.DisplayName, row.Url, CancellationToken.None);
                if (success)
                {
                    row.Installed = true;
                    ok++;
                }
                else
                {
                    Status = $"Failed to add \"{row.DisplayName}\": {output}";
                    return;
                }
            }
            Status = $"Added {ok} printer(s) to Windows. Try printing to one of them.";
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
