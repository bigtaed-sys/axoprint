using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.Shared;

namespace AxoPrint.Agent.Services;

public enum WorkerStatus { NotConfigured, Connecting, Online, Error }

/// <summary>
/// Background loop on the printer host: registers local printers with the
/// relay, long-polls for jobs, downloads each document and prints it via
/// SumatraPDF, then reports the outcome.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PrintWorker(AgentConfig config)
{
    private readonly SumatraPrinter _printer = new(config);
    private readonly Dictionary<string, string> _queueToLocal = new(StringComparer.Ordinal);
    private volatile bool _reconnect;

    public event Action<string>? Log;
    public event Action<WorkerStatus, string?>? StatusChanged;

    public WorkerStatus Status { get; private set; } = WorkerStatus.NotConfigured;

    /// <summary>Forces the loop to rebuild the relay client (after settings change).</summary>
    public void Reconnect() => _reconnect = true;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!config.IsConfigured)
            {
                SetStatus(WorkerStatus.NotConfigured, "Set relay URL and token in Settings.");
                await SafeDelay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            _reconnect = false;
            try
            {
                await SessionAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus(WorkerStatus.Error, ex.Message);
                Log?.Invoke($"Connection error: {ex.Message}");
                await SafeDelay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task SessionAsync(CancellationToken ct)
    {
        SetStatus(WorkerStatus.Connecting, null);
        var client = new RelayClient(config);

        var printers = WindowsPrinters.Enumerate(config.ExcludedPrinters);
        RebuildMap(printers);
        await client.RegisterAsync(printers, ct);
        var lastRegister = DateTimeOffset.UtcNow;

        SetStatus(WorkerStatus.Online, null);
        Log?.Invoke($"Registered {printers.Count} printer(s) with relay.");

        while (!ct.IsCancellationRequested && !_reconnect)
        {
            var job = await client.PullJobAsync(ct);
            if (job is not null)
                await HandleJobAsync(job, client, ct);

            if (DateTimeOffset.UtcNow - lastRegister > TimeSpan.FromMinutes(5))
            {
                printers = WindowsPrinters.Enumerate(config.ExcludedPrinters);
                RebuildMap(printers);
                await client.RegisterAsync(printers, ct);
                lastRegister = DateTimeOffset.UtcNow;
            }
        }
    }

    private async Task HandleJobAsync(JobEnvelope job, RelayClient client, CancellationToken ct)
    {
        Log?.Invoke($"Job {job.JobId}: \"{job.JobName}\" → {job.QueueId} ({job.DocumentBytes} bytes)");

        if (!_queueToLocal.TryGetValue(job.QueueId, out var localName) ||
            !WindowsPrinters.Exists(localName))
        {
            Log?.Invoke($"Job {job.JobId}: printer '{job.QueueId}' not available — aborting.");
            await client.ReportStatusAsync(job.JobId, JobState.Aborted, "Target printer not found.", ct);
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "AxoPrint", "jobs");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{job.JobId}{ExtensionFor(job.DocumentFormat)}");

        try
        {
            await client.DownloadDocumentAsync(job.JobId, file, ct);
            await _printer.PrintAsync(localName, file, job, ct);
            await client.ReportStatusAsync(job.JobId, JobState.Completed, null, ct);
            Log?.Invoke($"Job {job.JobId}: printed on '{localName}'.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Job {job.JobId}: FAILED — {ex.Message}");
            try { await client.ReportStatusAsync(job.JobId, JobState.Aborted, ex.Message, ct); }
            catch { /* relay may be unreachable */ }
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private void RebuildMap(IReadOnlyList<PrinterDescriptor> printers)
    {
        _queueToLocal.Clear();
        foreach (var p in printers)
            _queueToLocal[p.QueueId] = p.LocalName;
    }

    private static string ExtensionFor(string format) => format switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => ".bin",
    };

    private void SetStatus(WorkerStatus status, string? detail)
    {
        Status = status;
        StatusChanged?.Invoke(status, detail);
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }
}
