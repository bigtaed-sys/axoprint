using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.Setup.Services;

/// <summary>
/// Creates local Windows printers using the in-box "Microsoft Print to PDF"
/// driver, with a Local Port pointing at a file. Printing to one produces a PDF
/// silently in the spool folder, which the uploader then forwards to the relay.
/// No custom driver, no IPP-over-WAN, no print-policy issues.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsInstaller
{
    private const string Driver = "Microsoft Print To PDF";

    public static string PrinterName(string displayName) => "AxoPrint: " + displayName;

    public static IReadOnlySet<string> InstalledPrinterNames() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates/updates a local PDF printer whose output goes to <paramref name="portFile"/>.</summary>
    public static Task<(bool Ok, string Output)> AddLocalPrinterAsync(
        string displayName, string portFile, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(portFile)!);

        string vars =
            "$ErrorActionPreference = 'Stop'\n" +
            "$name = " + PsString(PrinterName(displayName)) + "\n" +
            "$port = " + PsString(portFile) + "\n" +
            "$driver = " + PsString(Driver) + "\n";

        const string body = """
            if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) {
                Add-PrinterPort -Name $port
            }
            if (Get-Printer -Name $name -ErrorAction SilentlyContinue) {
                Set-Printer -Name $name -PortName $port
            } else {
                Add-Printer -Name $name -DriverName $driver -PortName $port
            }
            Write-Output "OK"
            """;
        return RunPowerShellAsync(vars + body, ct);
    }

    public static Task<(bool Ok, string Output)> RemovePrinterAsync(
        string displayName, string portFile, CancellationToken ct)
    {
        string vars =
            "$ErrorActionPreference = 'SilentlyContinue'\n" +
            "$name = " + PsString(PrinterName(displayName)) + "\n" +
            "$port = " + PsString(portFile) + "\n";
        const string body = """
            Remove-Printer -Name $name
            Remove-PrinterPort -Name $port
            Write-Output "removed"
            """;
        return RunPowerShellAsync(vars + body, ct);
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

        return (proc.ExitCode == 0, (stdout + " " + stderr).Trim());
    }

    private static string PsString(string value) => "'" + value.Replace("'", "''") + "'";
}
