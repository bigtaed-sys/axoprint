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
/// Adds/removes printers in Windows that point at the relay over IPP, using
/// the in-box "Microsoft IPP Class Driver" (no vendor driver required).
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsInstaller
{
    private const string Driver = "Microsoft IPP Class Driver";

    public static IReadOnlySet<string> InstalledPrinterNames() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static Task<(bool Ok, string Output)> AddPrinterAsync(
        string displayName, string url, CancellationToken ct)
    {
        // Variables are defined first (safely quoted) so the script body can be
        // a plain raw string with the literal braces PowerShell if/else needs.
        string vars =
            "$ErrorActionPreference = 'Stop'\n" +
            "$name = " + PsString(displayName) + "\n" +
            "$url = " + PsString(url) + "\n" +
            "$driver = " + PsString(Driver) + "\n";

        const string body = """
            if (-not (Get-PrinterPort -Name $url -ErrorAction SilentlyContinue)) {
                Add-PrinterPort -Name $url
            }
            if (Get-Printer -Name $name -ErrorAction SilentlyContinue) {
                Set-Printer -Name $name -PortName $url
            } else {
                Add-Printer -Name $name -DriverName $driver -PortName $url
            }
            Write-Output "OK: $name -> $url"
            """;
        return RunPowerShellAsync(vars + body, ct);
    }

    public static Task<(bool Ok, string Output)> RemovePrinterAsync(string displayName, CancellationToken ct)
    {
        string script =
            "$ErrorActionPreference = 'Stop'\n" +
            "$name = " + PsString(displayName) + "\n" +
            "Remove-Printer -Name $name -ErrorAction SilentlyContinue\n" +
            "Write-Output \"Removed: $name\"\n";
        return RunPowerShellAsync(script, ct);
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
        await proc.WaitForExitAsync(ct);

        string output = (stdout + stderr).Trim();
        return (proc.ExitCode == 0, output);
    }

    /// <summary>Quotes a value as a single-quoted PowerShell string literal.</summary>
    private static string PsString(string value) => "'" + value.Replace("'", "''") + "'";
}
