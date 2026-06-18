using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AxoPrint.LinuxClient;

public sealed class PrinterRow : INotifyPropertyChanged
{
    public required string QueueId { get; init; }
    public required string DisplayName { get; init; }
    public required string Url { get; init; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private bool _installed;
    public bool Installed { get => _installed; set { Set(ref _installed, value); OnPropertyChanged(nameof(StatusLabel)); } }

    private bool _agentOnline;
    public bool AgentOnline { get => _agentOnline; set { Set(ref _agentOnline, value); OnPropertyChanged(nameof(StatusLabel)); } }

    private bool _duplex;
    public bool Duplex { get => _duplex; set => Set(ref _duplex, value); }

    private bool _monochrome;
    public bool Monochrome { get => _monochrome; set => Set(ref _monochrome, value); }

    public string StatusLabel =>
        (Installed ? "✓ установлен" : "не установлен") + (AgentOnline ? "" : " · агент офлайн");

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
