using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.LinuxClient.Services;

/// <summary>
/// Adds/removes CUPS print queues that point at the relay over IPP Everywhere.
/// CUPS connects to the URL directly and prints PDF — no local driver, no
/// resident uploader.
///
/// lpadmin is run directly (works without a password if the user is in the
/// CUPS admin group, e.g. lpadmin/sys/wheel). If that is refused, the caller
/// falls back to a generated script the user runs once with sudo — we avoid
/// pkexec, which needs a graphical polkit agent that isn't always present.
/// </summary>
public static class CupsInstaller
{
    public static string CupsName(string queueId) =>
        "AxoPrint_" + Regex.Replace(queueId, "[^A-Za-z0-9_]", "_");

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

    private static List<string> AddArgs(string queueId, string displayName, string url, bool duplex, bool monochrome) =>
        new()
        {
            "-p", CupsName(queueId),
            "-E",                          // enable + accept jobs
            "-v", ToIppUri(url),
            "-m", "everywhere",            // driverless IPP Everywhere (queries the relay)
            "-D", displayName,
            "-o", "printer-is-shared=false",
            "-o", duplex ? "sides-default=two-sided-long-edge" : "sides-default=one-sided",
            "-o", monochrome ? "print-color-mode-default=monochrome" : "print-color-mode-default=color",
        };

    /// <summary>Shell command (for the manual sudo fallback / display).</summary>
    public static string AddCommand(string queueId, string displayName, string url, bool duplex, bool monochrome) =>
        "lpadmin " + string.Join(' ', AddArgs(queueId, displayName, url, duplex, monochrome).Select(Quote));

    public static Task<(bool Ok, string Output)> AddAsync(
        string queueId, string displayName, string url, bool duplex, bool monochrome, CancellationToken ct) =>
        Task.Run(() => Run(Resolve("lpadmin"), AddArgs(queueId, displayName, url, duplex, monochrome)), ct);

    public static Task<(bool Ok, string Output)> RemoveAsync(string queueId, CancellationToken ct) =>
        Task.Run(() => Run(Resolve("lpadmin"), new[] { "-x", CupsName(queueId) }), ct);

    /// <summary>Writes a script that adds all the given printers; user runs it with sudo.</summary>
    public static string WriteSudoScript(IEnumerable<(string QueueId, string DisplayName, string Url, bool Duplex, bool Mono)> printers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("set -e");
        foreach (var p in printers)
            sb.AppendLine(AddCommand(p.QueueId, p.DisplayName, p.Url, p.Duplex, p.Mono));
        sb.AppendLine("echo 'AxoPrint: printers added.'");

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "axoprint");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "add-printers.sh");
        File.WriteAllText(path, sb.ToString().Replace("\r\n", "\n"));
        return path;
    }

    private static string Quote(string s) =>
        s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_=:./".Contains(c))
            ? s : "'" + s.Replace("'", "'\\''") + "'";

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
