using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.LinuxClient.Services;

/// <summary>
/// Adds/removes CUPS print queues that point at the relay over IPP Everywhere.
/// CUPS connects to the URL directly and prints PDF — no local driver, no
/// resident uploader. Privileged commands run via pkexec (graphical sudo).
/// </summary>
public static class CupsInstaller
{
    /// <summary>CUPS-safe queue name for a relay queue id.</summary>
    public static string CupsName(string queueId) =>
        "AxoPrint_" + Regex.Replace(queueId, "[^A-Za-z0-9_]", "_");

    /// <summary>https://… → ipps://…, http://… → ipp://… (CUPS device URI scheme).</summary>
    public static string ToIppUri(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "ipps://" + url["https://".Length..];
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "ipp://" + url["http://".Length..];
        return url;
    }

    public static IReadOnlySet<string> InstalledQueues()
    {
        var (ok, output) = Run("lpstat", new[] { "-e" });
        if (!ok)
            return new HashSet<string>();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static Task<(bool Ok, string Output)> AddAsync(
        string queueId, string displayName, string url, bool duplex, bool monochrome, CancellationToken ct)
    {
        string name = CupsName(queueId);
        string uri = ToIppUri(url);

        var args = new List<string>
        {
            Resolve("lpadmin"),
            "-p", name,
            "-E",                          // enable + accept jobs
            "-v", uri,
            "-m", "everywhere",            // driverless IPP Everywhere (queries the relay)
            "-D", displayName,
            "-o", "printer-is-shared=false",
            "-o", duplex ? "sides-default=two-sided-long-edge" : "sides-default=one-sided",
            "-o", monochrome ? "print-color-mode-default=monochrome" : "print-color-mode-default=color",
        };

        // pkexec runs lpadmin as root after a graphical auth prompt.
        return Task.Run(() => Run("pkexec", args), ct);
    }

    public static Task<(bool Ok, string Output)> RemoveAsync(string queueId, CancellationToken ct)
    {
        var args = new List<string> { Resolve("lpadmin"), "-x", CupsName(queueId) };
        return Task.Run(() => Run("pkexec", args), ct);
    }

    private static string Resolve(string bin)
    {
        var (ok, path) = Run("which", new[] { bin });
        if (ok && !string.IsNullOrWhiteSpace(path))
            return path.Trim().Split('\n')[0].Trim();
        foreach (var dir in new[] { "/usr/sbin", "/usr/bin", "/sbin", "/bin" })
        {
            string p = Path.Combine(dir, bin);
            if (File.Exists(p)) return p;
        }
        return bin;
    }

    private static (bool Ok, string Output) Run(string file, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode == 0, o.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
