using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.Setup.Services;

/// <summary>
/// Watches the spool folder for PDFs produced by the local printers and
/// forwards each to the relay as a print job for its queue.
///
/// Each printer writes to a fixed port file (&lt;queueId&gt;.pdf). To avoid a job
/// being overwritten by the next one, a completed port file is immediately
/// "picked up" — moved to a uniquely-named file in a pickup subfolder — and
/// only then uploaded (with retries). A FileSystemWatcher makes pickup near
/// instant; a poll loop is the backstop.
/// </summary>
public sealed class Uploader(SetupConfig config)
{
    public event Action<string>? Log;

    private static string Root => SetupConfig.SpoolDir;
    private static string PickupDir => Path.Combine(SetupConfig.SpoolDir, "queue");
    private const string Sep = "__";

    public async Task RunAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(PickupDir);
        var api = new RelayApi(config);

        using var watcher = new FileSystemWatcher(Root, "*.pdf")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        watcher.Created += (_, e) => TryPickup(e.FullPath);
        watcher.Changed += (_, e) => TryPickup(e.FullPath);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                PickupStray();
                if (config.IsConfigured)
                    await UploadQueuedAsync(api, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Log?.Invoke("Sweep error: " + ex.Message); }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Moves a finished port file into the pickup folder under a unique name.</summary>
    private void TryPickup(string portFile)
    {
        try
        {
            if (!IsComplete(portFile))
                return;
            string queueId = Path.GetFileNameWithoutExtension(portFile);
            string dest = Path.Combine(PickupDir, $"{queueId}{Sep}{Guid.NewGuid():N}.pdf");
            File.Move(portFile, dest);
        }
        catch (IOException) { /* still being written, or already moved */ }
        catch (Exception ex) { Log?.Invoke("Pickup error: " + ex.Message); }
    }

    private void PickupStray()
    {
        foreach (var f in Directory.GetFiles(Root, "*.pdf"))
            TryPickup(f);
    }

    private async Task UploadQueuedAsync(RelayApi api, CancellationToken ct)
    {
        foreach (var file in Directory.GetFiles(PickupDir, "*.pdf"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            int sep = name.IndexOf(Sep, StringComparison.Ordinal);
            string queueId = sep > 0 ? name[..sep] : name;

            try
            {
                await api.PrintAsync(queueId, file, config.OptionsFor(queueId), ct);
                TryDelete(file);
                Log?.Invoke($"Sent print job to \"{queueId}\".");
            }
            catch (HttpRequestException ex) when (ex.StatusCode is { } sc && (int)sc is >= 400 and < 500)
            {
                string bad = file + ".failed";
                TryDelete(bad);
                try { File.Move(file, bad); } catch { }
                Log?.Invoke($"Rejected \"{queueId}\" ({(int)sc}); moved to {Path.GetFileName(bad)}.");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Upload of \"{queueId}\" failed, will retry: {ex.Message}");
            }
        }
    }

    /// <summary>True once the spooler has released the file (write finished).</summary>
    private static bool IsComplete(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return fs.Length > 0;
        }
        catch (IOException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
