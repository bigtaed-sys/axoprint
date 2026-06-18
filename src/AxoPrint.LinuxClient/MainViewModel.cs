using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.LinuxClient.Services;

namespace AxoPrint.LinuxClient;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly LinuxConfig _config;

    public MainViewModel(LinuxConfig config)
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

    private string _status = "Введите адрес релея и токен, затем «Подключить».";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _log = "";
    public string Log { get => _log; private set => Set(ref _log, value); }

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

            Status = "Подключение…";
            var items = await new RelayApi(_config).GetPrintersAsync(CancellationToken.None);
            var installed = CupsInstaller.InstalledQueues();

            Printers.Clear();
            foreach (var p in items)
            {
                Printers.Add(new PrinterRow
                {
                    QueueId = p.QueueId,
                    DisplayName = p.DisplayName,
                    Url = p.Url,
                    Installed = installed.Contains(CupsInstaller.CupsName(p.QueueId)),
                    AgentOnline = p.AgentOnline,
                });
            }

            Status = items.Count == 0
                ? "Подключено, но агент ещё не зарегистрировал принтеры."
                : $"Найдено принтеров: {items.Count}." + (items.Any(i => i.AgentOnline) ? "" : " (агент офлайн)");
        }
        catch (Exception ex)
        {
            Status = "Ошибка: " + ex.Message;
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
            Status = "Отметьте хотя бы один принтер.";
            return;
        }

        Busy = true;
        try
        {
            int ok = 0;
            foreach (var row in chosen)
            {
                Status = $"Добавляю «{row.DisplayName}» в CUPS…";
                var (success, output) = await CupsInstaller.AddAsync(
                    row.QueueId, row.DisplayName, row.Url, row.Duplex, row.Monochrome, CancellationToken.None);
                if (success)
                {
                    row.Installed = true;
                    ok++;
                    AppendLog($"Добавлен принтер CUPS «{CupsInstaller.CupsName(row.QueueId)}».");
                }
                else
                {
                    Status = $"Не удалось добавить «{row.DisplayName}»: {output}";
                    AppendLog($"Ошибка lpadmin: {output}");
                    return;
                }
            }
            Status = $"Добавлено принтеров: {ok}. Печатайте в них из любого приложения.";
        }
        catch (Exception ex)
        {
            Status = "Ошибка: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
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
