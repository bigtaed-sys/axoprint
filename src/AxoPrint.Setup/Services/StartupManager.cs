using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace AxoPrint.Setup.Services;

/// <summary>Registers/unregisters a logon scheduled task so the client autostarts.
/// Runs with highest privileges (the client manages printers and the spool).</summary>
[SupportedOSPlatform("windows")]
public static class StartupManager
{
    private const string TaskName = "AxoPrintClient";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName).ok;

    public static (bool ok, string output) SetEnabled(bool enable)
    {
        if (!enable)
            return Run("/Delete", "/TN", TaskName, "/F");

        string exe = Environment.ProcessPath ?? "";
        return Run("/Create", "/TN", TaskName, "/TR", $"\"{exe}\"",
            "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");
    }

    private static (bool ok, string output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
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
}
