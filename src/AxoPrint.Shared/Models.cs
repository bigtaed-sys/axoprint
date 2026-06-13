namespace AxoPrint.Shared;

/// <summary>Lifecycle of a print job as tracked by the relay.</summary>
public enum JobState
{
    Pending = 3,      // queued on the relay, waiting for the agent to pull it
    Processing = 5,   // agent has pulled it and is printing
    Completed = 9,
    Canceled = 7,
    Aborted = 8,
}

/// <summary>
/// A printer the agent exposes. The agent fills this from the local Windows
/// spooler; the relay turns each one into an IPP queue.
/// </summary>
public sealed record PrinterDescriptor
{
    /// <summary>Stable, URL-safe queue id (slug of the Windows printer name).</summary>
    public required string QueueId { get; init; }

    /// <summary>Human-friendly name shown to the user / in Windows.</summary>
    public required string DisplayName { get; init; }

    public string MakeAndModel { get; init; } = "AxoPrint Remote Printer";
    public string Location { get; init; } = "";

    /// <summary>True if the printer renders colour (drives print-color-mode-supported).</summary>
    public bool Color { get; init; } = true;

    /// <summary>True if the printer can duplex.</summary>
    public bool Duplex { get; init; }

    /// <summary>Native Windows printer name on the agent host (not sent to clients).</summary>
    public string LocalName { get; init; } = "";
}

/// <summary>Payload the agent POSTs to register/refresh its printers.</summary>
public sealed record RegisterRequest
{
    public required string AgentId { get; init; }
    public string AgentVersion { get; init; } = "";
    public required IReadOnlyList<PrinterDescriptor> Printers { get; init; }
}

/// <summary>Job metadata handed to the agent when it pulls work.</summary>
public sealed record JobEnvelope
{
    public required string JobId { get; init; }
    public required string QueueId { get; init; }
    public required string DocumentFormat { get; init; }
    public string JobName { get; init; } = "";
    public string UserName { get; init; } = "";
    public int Copies { get; init; } = 1;
    public bool Duplex { get; init; }
    public bool Color { get; init; } = true;
    public string Media { get; init; } = "";
    public long DocumentBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Status update the agent POSTs back as it prints.</summary>
public sealed record JobStatusUpdate
{
    public required JobState State { get; init; }
    public string? Message { get; init; }
}

/// <summary>Printer entry returned to the sender-side setup client.</summary>
public sealed record PrinterListItem
{
    public required string QueueId { get; init; }
    public required string DisplayName { get; init; }
    /// <summary>HTTP(S) URL Windows uses as the IPP printer port.</summary>
    public required string Url { get; init; }
    public bool AgentOnline { get; init; }
}

public static class Slug
{
    /// <summary>Lower-cased, URL/IPP-safe queue id derived from a printer name.</summary>
    public static string From(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "printer" : slug;
    }
}
