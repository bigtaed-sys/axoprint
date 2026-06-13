using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.Setup.Services;

/// <summary>
/// Adds/removes IPP printers in Windows via <c>Add-Printer -IppURL</c>. This is
/// the supported path for driverless IPP Everywhere: Windows auto-selects the
/// in-box "Microsoft IPP Class Driver", no port or driver is specified, and it
/// works under Windows Protected Print Mode (unlike -PortName/-DriverName or
/// AddPrinterConnection2, which fail for an HTTP(S) URL).
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsInstaller
{
    public static IReadOnlySet<string> InstalledPrinterNames() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static Task<(bool Ok, string Output)> AddPrinterAsync(
        string displayName, string url, CancellationToken ct)
    {
        // Add-Printer expects the IPP scheme (ipp/ipps), not http/https.
        string ippUrl = ToIppScheme(url);
        string script =
            "$ErrorActionPreference = 'Stop'\n" +
            "Add-Printer -IppURL " + PsString(ippUrl) + "\n" +
            "Write-Output 'OK'\n";
        return RunPowerShellAsync(script, ct);
    }

    public static Task<(bool Ok, string Output)> RemovePrinterAsync(
        string displayName, CancellationToken ct)
    {
        string script =
            "$ErrorActionPreference = 'Stop'\n" +
            "Remove-Printer -Name " + PsString(displayName) + "\n" +
            "Write-Output 'removed'\n";
        return RunPowerShellAsync(script, ct);
    }

    private static string ToIppScheme(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "ipps://" + url["https://".Length..];
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "ipp://" + url["http://".Length..];
        return url;
    }

    private static async Task<(bool Ok, string Output)> RunPowerShellAsync(string script, CancellationToken ct)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        string stderr = await proc.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } }

        string output = (stdout + " " + stderr).Trim();
        return (proc.ExitCode == 0, output);
    }

    /// <summary>Quotes a value as a single-quoted PowerShell string literal.</summary>
    private static string PsString(string value) => "'" + value.Replace("'", "''") + "'";
}
