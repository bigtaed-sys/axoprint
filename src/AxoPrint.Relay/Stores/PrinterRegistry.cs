using System.Collections.Concurrent;
using System.Text.Json;
using AxoPrint.Shared;

namespace AxoPrint.Relay.Stores;

/// <summary>
/// Holds the set of IPP queues the agent has registered. Persisted to disk so
/// Windows can still query/queue against a printer while the agent is offline.
/// </summary>
public sealed class PrinterRegistry
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, PrinterDescriptor> _printers = new();
    private volatile string? _agentId;
    private DateTimeOffset _lastSeen;

    public PrinterRegistry(IConfiguration config, IHostEnvironment env)
    {
        var dataDir = config["Axo:DataDir"] is { } d && !string.IsNullOrWhiteSpace(d)
            ? d : Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "printers.json");
        Load();
    }

    public string? AgentId => _agentId;
    public DateTimeOffset LastSeen => _lastSeen;
    public bool AgentOnline => DateTimeOffset.UtcNow - _lastSeen < TimeSpan.FromSeconds(90);

    public IReadOnlyCollection<PrinterDescriptor> All => _printers.Values.ToArray();

    public PrinterDescriptor? Get(string queueId) =>
        _printers.TryGetValue(queueId, out var p) ? p : null;

    public IReadOnlyCollection<string> QueueIds => _printers.Keys.ToArray();

    public void Register(RegisterRequest request)
    {
        lock (_gate)
        {
            _agentId = request.AgentId;
            _lastSeen = DateTimeOffset.UtcNow;
            _printers.Clear();
            foreach (var p in request.Printers)
                _printers[p.QueueId] = p;
            Save();
        }
    }

    public void Heartbeat() => _lastSeen = DateTimeOffset.UtcNow;

    private void Load()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            var list = JsonSerializer.Deserialize<List<PrinterDescriptor>>(File.ReadAllText(_path));
            foreach (var p in list ?? new())
                _printers[p.QueueId] = p;
        }
        catch { /* corrupt file: start empty */ }
    }

    private void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_printers.Values.ToList(),
            new JsonSerializerOptions { WriteIndented = true }));
}
