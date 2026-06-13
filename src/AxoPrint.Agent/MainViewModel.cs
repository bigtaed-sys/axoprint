using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AxoPrint.Agent.Services;
using Avalonia.Threading;

namespace AxoPrint.Agent;

/// <summary>Backs the status/settings window. Reflects live worker state.</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AgentConfig _config;
    private readonly PrintWorker _worker;

    public MainViewModel(AgentConfig config, PrintWorker worker)
    {
        _config = config;
        _worker = worker;
        _relayBaseUrl = config.RelayBaseUrl;
        _token = config.Token;
        _sumatraPath = config.SumatraPath;

        _worker.StatusChanged += (status, detail) =>
            Dispatcher.UIThread.Post(() => UpdateStatus(status, detail));
        _worker.Log += line =>
            Dispatcher.UIThread.Post(() => AppendLog(line));
    }

    private string _relayBaseUrl;
    public string RelayBaseUrl { get => _relayBaseUrl; set => Set(ref _relayBaseUrl, value); }

    private string _token;
    public string Token { get => _token; set => Set(ref _token, value); }

    private string _sumatraPath;
    public string SumatraPath { get => _sumatraPath; set => Set(ref _sumatraPath, value); }

    private string _statusText = "Starting…";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private string _log = "";
    public string Log { get => _log; private set => Set(ref _log, value); }

    private string _saveHint = "";
    public string SaveHint { get => _saveHint; private set => Set(ref _saveHint, value); }

    /// <summary>Persists edited settings and reconnects the worker.</summary>
    public void Save()
    {
        _config.RelayBaseUrl = RelayBaseUrl.Trim();
        _config.Token = Token.Trim();
        _config.SumatraPath = SumatraPath.Trim();
        _config.Save();
        _worker.Reconnect();
        SaveHint = $"Saved {DateTime.Now:HH:mm:ss}. Reconnecting…";
    }

    private void UpdateStatus(WorkerStatus status, string? detail)
    {
        StatusText = status switch
        {
            WorkerStatus.NotConfigured => "⚙ Not configured — " + (detail ?? ""),
            WorkerStatus.Connecting => "… Connecting to relay",
            WorkerStatus.Online => "● Online — waiting for jobs",
            WorkerStatus.Error => "✕ Error — " + (detail ?? ""),
            _ => status.ToString(),
        };
    }

    private void AppendLog(string line)
    {
        string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        Log = string.IsNullOrEmpty(Log) ? stamped : stamped + "\n" + Log;
        // Keep the log bounded.
        if (Log.Length > 16000)
            Log = Log[..16000];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
