using System;
using System.IO;
using System.Text.Json;

namespace AxoPrint.LinuxClient.Services;

/// <summary>Client settings, persisted to ~/.config/axoprint/client.json.</summary>
public sealed class LinuxConfig
{
    public string RelayBaseUrl { get; set; } = "";
    public string Token { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RelayBaseUrl) && !string.IsNullOrWhiteSpace(Token);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "axoprint");

    public static string DefaultPath => Path.Combine(DefaultDir, "client.json");

    public static LinuxConfig Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
                return JsonSerializer.Deserialize<LinuxConfig>(File.ReadAllText(DefaultPath)) ?? new();
        }
        catch { }
        return new LinuxConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(DefaultDir);
        File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
