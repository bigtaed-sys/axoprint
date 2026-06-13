using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.Shared;

namespace AxoPrint.Agent.Services;

/// <summary>
/// Prints a document to a named Windows printer by invoking SumatraPDF in
/// silent mode. SumatraPDF renders PDF/XPS/images faithfully and supports
/// headless printing via the command line.
/// </summary>
public sealed class SumatraPrinter(AgentConfig config)
{
    public string? Resolve()
    {
        if (!string.IsNullOrWhiteSpace(config.SumatraPath) && File.Exists(config.SumatraPath))
            return config.SumatraPath;

        foreach (var candidate in Candidates())
            if (File.Exists(candidate))
                return candidate;

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "SumatraPDF.exe");
        yield return Path.Combine(baseDir, "tools", "SumatraPDF.exe");

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(pf, "SumatraPDF", "SumatraPDF.exe");
        yield return Path.Combine(local, "SumatraPDF", "SumatraPDF.exe");
    }

    /// <summary>Builds SumatraPDF's -print-settings token list from job options.</summary>
    public static string BuildPrintSettings(JobEnvelope job)
    {
        var tokens = new List<string> { "fit" };
        if (job.Copies > 1)
            tokens.Add($"{job.Copies}x");
        tokens.Add(job.Duplex ? "duplexlong" : "simplex");
        tokens.Add(job.Color ? "color" : "monochrome");
        return string.Join(',', tokens);
    }

    public async Task PrintAsync(string printerName, string documentPath, JobEnvelope job, CancellationToken ct)
    {
        string? exe = Resolve();
        if (exe is null)
            throw new FileNotFoundException(
                "SumatraPDF.exe not found. Set its path in settings or place it next to the agent.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-print-to");
        psi.ArgumentList.Add(printerName);
        psi.ArgumentList.Add("-print-settings");
        psi.ArgumentList.Add(BuildPrintSettings(job));
        psi.ArgumentList.Add("-silent");
        psi.ArgumentList.Add("-exit-when-done");
        psi.ArgumentList.Add(documentPath);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await proc.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            throw new TimeoutException("SumatraPDF did not finish printing within 5 minutes.");
        }

        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException(
                $"SumatraPDF exited with code {proc.ExitCode}. {err}".Trim());
        }
    }
}
