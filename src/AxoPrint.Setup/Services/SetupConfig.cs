using System;
using System.IO;
using System.Text.Json;

namespace AxoPrint.Setup.Services;

/// <summary>Sender-side settings, persisted to %APPDATA%\AxoPrint\setup.json.</summary>
public sealed class SetupConfig
{
    public string RelayBaseUrl { get; set; } = "";
    public string Token { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AxoPrint");

    public static string DefaultPath => Path.Combine(DefaultDir, "setup.json");

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
