using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace AxoPrint.Agent.Services;

/// <summary>Registers/unregisters a logon scheduled task so the app autostarts.</summary>
[SupportedOSPlatform("windows")]
public static class StartupManager
{
    private const string TaskName = "AxoPrintAgent";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName).ok;

    public static (bool ok, string output) SetEnabled(bool enable)
    {
        if (!enable)
            return Run("/Delete", "/TN", TaskName, "/F");

        string exe = Environment.ProcessPath ?? "";
        // Agent needs no elevation, so a default run level is fine.
        return Run("/Create", "/TN", TaskName, "/TR", $"\"{exe}\"", "/SC", "ONLOGON", "/F");
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
