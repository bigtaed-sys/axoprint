using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.Versioning;
using AxoPrint.Shared;

namespace AxoPrint.Agent.Services;

/// <summary>Enumerates locally installed Windows printers and their capabilities.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsPrinters
{
    public static IReadOnlyList<PrinterDescriptor> Enumerate(IEnumerable<string> excluded)
    {
        var skip = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        var result = new List<PrinterDescriptor>();
        var seenQueueIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (skip.Contains(name))
                continue;

            var settings = new PrinterSettings { PrinterName = name };
            if (!settings.IsValid)
                continue;

            // Ensure a unique queue id even if two names slugify identically.
            string queueId = Slug.From(name);
            string unique = queueId;
            for (int i = 2; !seenQueueIds.Add(unique); i++)
                unique = $"{queueId}-{i}";

            result.Add(new PrinterDescriptor
            {
                QueueId = unique,
                DisplayName = name,
                LocalName = name,
                MakeAndModel = $"AxoPrint → {name}",
                Color = settings.SupportsColor,
                Duplex = settings.CanDuplex,
            });
        }

        return result;
    }

    public static bool Exists(string printerName) =>
        PrinterSettings.InstalledPrinters.Cast<string>()
            .Any(n => string.Equals(n, printerName, StringComparison.OrdinalIgnoreCase));
}
