using System;
using System.IO;
using System.Text.Json;

namespace AxoPrint.Setup.Services;

/// <summary>Sender-side settings, persisted to %APPDATA%\AxoPrint\setup.json.</summary>
public sealed class SetupConfig
{
    public string RelayBaseUrl { get; set; } = "";
    public string Token { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RelayBaseUrl) && !string.IsNullOrWhiteSpace(Token);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AxoPrint");

    public static string DefaultPath => Path.Combine(DefaultDir, "setup.json");

    /// <summary>Where the local "Microsoft Print to PDF" printers drop their output
    /// (one &lt;queueId&gt;.pdf per printer). The spooler (SYSTEM) writes here; the
    /// uploader reads and forwards. Machine-wide so it is stable across users.</summary>
    public static string SpoolDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AxoPrint", "spool");

    public static string PortFileFor(string queueId) => Path.Combine(SpoolDir, queueId + ".pdf");

    public static SetupConfig Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
                return JsonSerializer.Deserialize<SetupConfig>(File.ReadAllText(DefaultPath)) ?? new();
        }
        catch { }
        return new SetupConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(DefaultDir);
        File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
