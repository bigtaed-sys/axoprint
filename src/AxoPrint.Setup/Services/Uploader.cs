using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.Setup.Services;

/// <summary>
/// Watches the spool folder for PDFs produced by the local printers and
/// forwards each to the relay as a print job for its queue (the file name is
/// the queue id). Polls on a short interval so it never misses a file.
/// </summary>
public sealed class Uploader(SetupConfig config)
{
    public event Action<string>? Log;

    public async Task RunAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(SetupConfig.SpoolDir);
        var api = new RelayApi(config);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (config.IsConfigured)
                    await SweepAsync(api, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Log?.Invoke("Sweep error: " + ex.Message); }

            try { await Task.Delay(TimeSpan.FromSeconds(1.5), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(RelayApi api, CancellationToken ct)
    {
        foreach (var file in Directory.GetFiles(SetupConfig.SpoolDir, "*.pdf"))
        {
            if (!IsComplete(file))
                continue; // still being written by the spooler

            string queueId = Path.GetFileNameWithoutExtension(file);
            try
            {
                await api.PrintAsync(queueId, file, ct);
                TryDelete(file);
                Log?.Invoke($"Sent print job to \"{queueId}\".");
            }
            catch (HttpRequestException ex) when (ex.StatusCode is { } sc && (int)sc is >= 400 and < 500)
            {
                // Permanent (e.g. unknown queue): set aside so we don't loop forever.
                string bad = file + ".failed";
                TryDelete(bad);
                try { File.Move(file, bad); } catch { }
                Log?.Invoke($"Rejected \"{queueId}\" ({(int)sc}); moved to {Path.GetFileName(bad)}.");
            }
            catch (Exception ex)
            {
                // Transient (network/relay down): leave the file and retry next sweep.
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
