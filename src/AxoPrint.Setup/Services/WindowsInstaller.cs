using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace AxoPrint.Setup.Services;

/// <summary>
/// Adds/removes IPP printers in Windows that point at the relay, using the
/// spooler API <c>AddPrinterConnection2</c> in driverless mode — the same path
/// the "Select a shared printer by name" wizard uses. Windows negotiates the
/// in-box "Microsoft IPP Class Driver" from the relay's IPP attributes, so no
/// vendor driver and no printer port need to be created manually.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsInstaller
{
    // PRINTER_CONNECTION_INFO_1 flags (winspool.h).
    private const uint PRINTER_CONNECTION_NO_UI = 0x00000040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterConnectionInfo1
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pszDriverName;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AddPrinterConnection2W")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddPrinterConnection2(
        IntPtr hWnd, string pszName, uint dwLevel, ref PrinterConnectionInfo1 pConnectionInfo);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeletePrinterConnectionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeletePrinterConnection(string pName);

    public static IReadOnlySet<string> InstalledPrinterNames() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the printer by its IPP URL. Driverless (pszDriverName = null) so the
    /// spooler pulls the IPP Class Driver via Get-Printer-Attributes from the relay.
    /// </summary>
    public static Task<(bool Ok, string Output)> AddPrinterAsync(
        string displayName, string url, CancellationToken ct) => Task.Run(() =>
    {
        var info = new PrinterConnectionInfo1
        {
            dwFlags = PRINTER_CONNECTION_NO_UI,
            pszDriverName = null,
        };

        if (AddPrinterConnection2(IntPtr.Zero, url, 1, ref info))
            return (true, $"OK: {displayName}");

        int code = Marshal.GetLastWin32Error();
        return (false, $"0x{code:X8}: {new Win32Exception(code).Message} (url: {url})");
    }, ct);

    public static Task<(bool Ok, string Output)> RemovePrinterAsync(
        string url, CancellationToken ct) => Task.Run(() =>
    {
        if (DeletePrinterConnection(url))
            return (true, "removed");
        int code = Marshal.GetLastWin32Error();
        return (false, $"0x{code:X8}: {new Win32Exception(code).Message}");
    }, ct);
}
