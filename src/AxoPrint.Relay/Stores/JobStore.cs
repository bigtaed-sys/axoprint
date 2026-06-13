using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using AxoPrint.Shared;

namespace AxoPrint.Relay.Stores;

public sealed class JobRecord
{
    public int Id { get; set; }
    public string QueueId { get; set; } = "";
    public string JobName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string DocumentFormat { get; set; } = "application/pdf";
    public int Copies { get; set; } = 1;
    public bool Duplex { get; set; }
    public bool Color { get; set; } = true;
    public string Media { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JobState State { get; set; } = JobState.Pending;

    public string? StateMessage { get; set; }
    public long DocumentBytes { get; set; }

    /// <summary>False until the document has been received (Create-Job before Send-Document).</summary>
    public bool Ready { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Disk-backed queue of print jobs. Documents are stored as files; metadata as
/// JSON. Provides an async long-poll so the agent can wait for new work.
/// </summary>
public sealed class JobStore
{
    private readonly string _jobsDir;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<int, JobRecord> _jobs = new();
    private readonly List<TaskCompletionSource<bool>> _waiters = new();
    private int _nextId;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JobStore(IConfiguration config, IHostEnvironment env)
    {
        var dataDir = config["Axo:DataDir"] is { } d && !string.IsNullOrWhiteSpace(d)
            ? d : Path.Combine(env.ContentRootPath, "data");
        _jobsDir = Path.Combine(dataDir, "jobs");
        Directory.CreateDirectory(_jobsDir);
        Load();
    }

    public string DocumentPath(int id) => Path.Combine(_jobsDir, $"{id}.doc");
    private string MetaPath(int id) => Path.Combine(_jobsDir, $"{id}.json");

    public JobRecord? Get(int id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public IReadOnlyList<JobRecord> ForQueue(string queueId, Func<JobRecord, bool>? filter = null) =>
        _jobs.Values.Where(j => j.QueueId == queueId && (filter?.Invoke(j) ?? true))
            .OrderBy(j => j.Id).ToList();

    public int PendingCount(string queueId) =>
        _jobs.Values.Count(j => j.QueueId == queueId && j.State == JobState.Pending);

    /// <summary>Allocates a job id + record. Caller then writes the document and calls <see cref="Commit"/>.</summary>
    public JobRecord Create(string queueId, string jobName, string userName, string format,
        int copies, bool duplex, bool color, string media)
    {
        lock (_gate)
        {
            int id = ++_nextId;
            var job = new JobRecord
            {
                Id = id,
                QueueId = queueId,
                JobName = jobName,
                UserName = userName,
                DocumentFormat = format,
                Copies = copies,
                Duplex = duplex,
                Color = color,
                Media = media,
                State = JobState.Pending,
            };
            _jobs[id] = job;
            return job;
        }
    }

    /// <summary>Persists the job after its document has been written, and wakes pollers.</summary>
    public void Commit(JobRecord job, long documentBytes)
    {
        job.DocumentBytes = documentBytes;
        job.Ready = true;
        Persist(job);
        SignalWaiters();
    }

    public void UpdateState(int id, JobState state, string? message = null)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return;
        job.State = state;
        job.StateMessage = message;
        if (state is JobState.Completed or JobState.Canceled or JobState.Aborted)
            job.CompletedAt = DateTimeOffset.UtcNow;
        Persist(job);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for a pending job in one of the
    /// owned queues, marks it Processing and returns it; or null on timeout.
    /// </summary>
    public async Task<JobRecord?> WaitNextAsync(
        IReadOnlyCollection<string> ownedQueues, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!ct.IsCancellationRequested)
        {
            TaskCompletionSource<bool> waiter;
            lock (_gate)
            {
                var job = _jobs.Values
                    .Where(j => j.State == JobState.Pending && j.Ready && ownedQueues.Contains(j.QueueId))
                    .OrderBy(j => j.Id)
                    .FirstOrDefault();
                if (job is not null)
                {
                    job.State = JobState.Processing;
                    Persist(job);
                    return job;
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return null;

                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
            }

            var delay = deadline - DateTimeOffset.UtcNow;
            if (delay <= TimeSpan.Zero)
                return null;
            using var reg = ct.Register(() => waiter.TrySetResult(false));
            await Task.WhenAny(waiter.Task, Task.Delay(delay, CancellationToken.None));
        }
        return null;
    }

    private void SignalWaiters()
    {
        lock (_gate)
        {
            foreach (var w in _waiters)
                w.TrySetResult(true);
            _waiters.Clear();
        }
    }

    private void Persist(JobRecord job)
    {
        try { File.WriteAllText(MetaPath(job.Id), JsonSerializer.Serialize(job, JsonOpts)); }
        catch { /* best effort */ }
    }

    private void Load()
    {
        foreach (var file in Directory.EnumerateFiles(_jobsDir, "*.json"))
        {
            try
            {
                var job = JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(file));
                if (job is null) continue;
                // Jobs that were mid-flight when the relay restarted go back to pending.
                if (job.State == JobState.Processing)
                    job.State = JobState.Pending;
                _jobs[job.Id] = job;
                _nextId = Math.Max(_nextId, job.Id);
            }
            catch { /* skip corrupt */ }
        }
    }
}
