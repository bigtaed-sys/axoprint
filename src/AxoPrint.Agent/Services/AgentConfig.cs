using System;
using System.IO;
using System.Text.Json;

namespace AxoPrint.Agent.Services;

/// <summary>Agent settings, persisted to %APPDATA%\AxoPrint\agent.json.</summary>
public sealed class AgentConfig
{
    public string RelayBaseUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public string AgentId { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentName { get; set; } = Environment.MachineName;

    /// <summary>Optional explicit path to SumatraPDF.exe; auto-discovered if empty.</summary>
    public string SumatraPath { get; set; } = "";

    /// <summary>Long-poll wait seconds for jobs/next.</summary>
    public int PollSeconds { get; set; } = 25;

    /// <summary>Windows printer names to exclude (e.g. virtual PDF printers).</summary>
    public string[] ExcludedPrinters { get; set; } =
    {
        "Microsoft Print to PDF", "Microsoft XPS Document Writer",
        "OneNote (Desktop)", "OneNote for Windows 10", "Fax",
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AxoPrint");

    public static string DefaultPath => Path.Combine(DefaultDir, "agent.json");

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RelayBaseUrl) && !string.IsNullOrWhiteSpace(Token);

    public static AgentConfig Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
                return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(DefaultPath)) ?? new();
        }
        catch { /* fall through to defaults */ }
        return new AgentConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(DefaultDir);
        File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
