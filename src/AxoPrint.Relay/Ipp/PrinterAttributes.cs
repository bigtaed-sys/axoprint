using System.Security.Cryptography;
using AxoPrint.Ipp;
using AxoPrint.Shared;

namespace AxoPrint.Relay.Ipp;

/// <summary>
/// Builds the printer-attributes group returned by Get-Printer-Attributes.
/// The attribute set targets IPP Everywhere (PWG 5100.14) so that the
/// Microsoft IPP Class Driver installs the queue without a vendor driver.
/// </summary>
public static class PrinterAttributes
{
    // Media sizes advertised to clients (PWG self-describing names + dimensions in 1/100 mm).
    private static readonly (string Name, int X, int Y)[] Media =
    {
        ("iso_a4_210x297mm", 21000, 29700),
        ("na_letter_8.5x11in", 21590, 27940),
        ("na_legal_8.5x14in", 21590, 35560),
        ("iso_a5_148x210mm", 14800, 21000),
    };

    public static IppAttributeGroup Build(
        PrinterDescriptor printer,
        string printerUri,
        IppPrinterState state,
        bool acceptingJobs,
        int queuedJobCount,
        int upTimeSeconds,
        IReadOnlyCollection<string>? requested)
    {
        var all = new IppAttributeGroup(IppTag.PrinterAttributes);

        void A(IppAttribute attr) => all.Add(attr);

        A(new IppAttribute("printer-uri-supported", IppValue.Uri(printerUri)));
        A(IppAttribute.Keyword("uri-authentication-supported", "none"));
        A(IppAttribute.Keyword("uri-security-supported",
            printerUri.StartsWith("ipps", StringComparison.OrdinalIgnoreCase) ? "tls" : "none"));

        A(IppAttribute.NameW("printer-name", printer.DisplayName));
        A(IppAttribute.TextW("printer-info", printer.DisplayName));
        A(IppAttribute.TextW("printer-make-and-model", printer.MakeAndModel));
        A(IppAttribute.TextW("printer-location", printer.Location));
        A(new IppAttribute("printer-uuid", IppValue.Uri(Uuid(printer.QueueId))));

        A(IppAttribute.Enum("printer-state", (int)state));
        A(IppAttribute.Keyword("printer-state-reasons", "none"));
        A(IppAttribute.Boolean("printer-is-accepting-jobs", acceptingJobs));
        A(IppAttribute.Integer("queued-job-count", queuedJobCount));
        A(IppAttribute.Integer("printer-up-time", Math.Max(1, upTimeSeconds)));

        A(IppAttribute.Keyword("ipp-versions-supported", "1.1", "2.0"));
        A(IppAttribute.Keyword("ipp-features-supported", "ipp-everywhere"));
        A(new IppAttribute("operations-supported",
            new[]
            {
                IppOperation.PrintJob, IppOperation.ValidateJob, IppOperation.CreateJob,
                IppOperation.SendDocument, IppOperation.CancelJob, IppOperation.GetJobAttributes,
                IppOperation.GetJobs, IppOperation.GetPrinterAttributes, IppOperation.CloseJob,
            }.Select(o => IppValue.Enum((int)o)).ToArray()));

        A(IppAttribute.Charset("charset-configured", "utf-8"));
        A(IppAttribute.Charset("charset-supported", "utf-8"));
        A(IppAttribute.Language("natural-language-configured", "en"));
        A(IppAttribute.Language("generated-natural-language-supported", "en"));

        // Windows prefers PDF when offered, so it stays the default/spool format
        // even though we also advertise the mandatory IPP Everywhere raster
        // formats (PWG Raster / URF) required for the driverless install to validate.
        A(IppAttribute.Mime("document-format-default", "application/pdf"));
        A(IppAttribute.Mime("document-format-supported",
            "application/pdf", "image/pwg-raster", "image/urf", "image/jpeg", "application/octet-stream"));
        A(IppAttribute.Keyword("pdl-override-supported", "attempted"));
        A(IppAttribute.Keyword("compression-supported", "none"));
        A(IppAttribute.Boolean("color-supported", printer.Color));
        A(IppAttribute.Boolean("multiple-document-jobs-supported", false));
        A(IppAttribute.TextW("printer-device-id",
            $"MFG:AxoPrint;MDL:{printer.DisplayName};CMD:PDF,PWGRaster,URF;CLS:PRINTER;"));

        // PWG Raster capabilities — required because image/pwg-raster is advertised.
        A(new IppAttribute("pwg-raster-document-resolution-supported",
            IppValue.Resolution(IppResolution.Dpi(300)), IppValue.Resolution(IppResolution.Dpi(600))));
        A(IppAttribute.Keyword("pwg-raster-document-type-supported",
            "black_1", "sgray_8", "srgb_8"));
        A(IppAttribute.Keyword("pwg-raster-document-sheet-back", "normal"));

        // URF capabilities — required because image/urf is advertised (AirPrint/Mopria).
        A(IppAttribute.Keyword("urf-supported",
            "CP1", "IS1", "MT1-2-3-4-5-6", "OB10", "PQ4", "RS300-600",
            "SRGB24", "V1.4", "W8", "DM1"));

        A(IppAttribute.Keyword("job-creation-attributes-supported",
            "copies", "sides", "media", "media-col", "print-color-mode",
            "print-quality", "printer-resolution", "orientation-requested"));

        // Media -----------------------------------------------------------
        A(IppAttribute.Keyword("media-default", Media[0].Name));
        A(IppAttribute.Keyword("media-supported", Media.Select(m => m.Name).ToArray()));
        A(IppAttribute.Keyword("media-ready", Media[0].Name));
        A(IppAttribute.Keyword("media-col-supported",
            "media-size", "media-type", "media-source",
            "media-top-margin", "media-bottom-margin", "media-left-margin", "media-right-margin"));
        A(IppAttribute.Keyword("media-type-supported", "stationery"));
        A(IppAttribute.Keyword("media-source-supported", "auto"));
        // Margins (hundredths of a mm). CUPS' "everywhere" PPD generator needs the
        // supported-margin attributes + margins inside media-col to compute the
        // printable area, else lpadmin fails with "does not support required IPP
        // attributes". Advertise borderless (0) and a normal ~4.2 mm margin.
        A(IppAttribute.Integer("media-bottom-margin-supported", 0, Margin));
        A(IppAttribute.Integer("media-top-margin-supported", 0, Margin));
        A(IppAttribute.Integer("media-left-margin-supported", 0, Margin));
        A(IppAttribute.Integer("media-right-margin-supported", 0, Margin));
        A(new IppAttribute("media-col-default", IppValue.Collection(MediaCol(Media[0]))));
        A(new IppAttribute("media-col-database",
            Media.Select(m => IppValue.Collection(MediaCol(m))).ToArray()));

        // Duplex ----------------------------------------------------------
        A(IppAttribute.Keyword("sides-default", "one-sided"));
        A(printer.Duplex
            ? IppAttribute.Keyword("sides-supported", "one-sided", "two-sided-long-edge", "two-sided-short-edge")
            : IppAttribute.Keyword("sides-supported", "one-sided"));

        // Colour ----------------------------------------------------------
        A(IppAttribute.Keyword("print-color-mode-default", printer.Color ? "color" : "monochrome"));
        A(printer.Color
            ? IppAttribute.Keyword("print-color-mode-supported", "color", "monochrome", "auto")
            : IppAttribute.Keyword("print-color-mode-supported", "monochrome"));

        // Quality / resolution -------------------------------------------
        A(IppAttribute.Enum("print-quality-default", 4));
        A(new IppAttribute("print-quality-supported",
            IppValue.Enum(3), IppValue.Enum(4), IppValue.Enum(5)));
        A(new IppAttribute("printer-resolution-default", IppValue.Resolution(IppResolution.Dpi(600))));
        A(new IppAttribute("printer-resolution-supported",
            IppValue.Resolution(IppResolution.Dpi(300)), IppValue.Resolution(IppResolution.Dpi(600))));

        // Misc ------------------------------------------------------------
        A(new IppAttribute("orientation-requested-supported",
            IppValue.Enum(3), IppValue.Enum(4), IppValue.Enum(5), IppValue.Enum(6)));
        A(IppAttribute.Integer("copies-default", 1));
        A(new IppAttribute("copies-supported", IppValue.Range(1, 999)));
        A(IppAttribute.Enum("finishings-default", 3));
        A(IppAttribute.Enum("finishings-supported", 3));
        A(IppAttribute.Keyword("output-bin-default", "face-down"));
        A(IppAttribute.Keyword("output-bin-supported", "face-down"));
        A(IppAttribute.Keyword("job-sheets-default", "none"));
        A(IppAttribute.Keyword("job-sheets-supported", "none"));
        A(IppAttribute.Keyword("which-jobs-supported", "completed", "not-completed"));

        return Filter(all, requested);
    }

    // Normal print margin in hundredths of a mm (~4.2 mm).
    private const int Margin = 423;

    private static IppCollection MediaCol((string Name, int X, int Y) m) =>
        new IppCollection()
            .Add(new IppAttribute("media-size", IppValue.Collection(
                new IppCollection()
                    .Add(IppAttribute.Integer("x-dimension", m.X))
                    .Add(IppAttribute.Integer("y-dimension", m.Y)))))
            .Add(IppAttribute.Keyword("media-type", "stationery"))
            .Add(IppAttribute.Keyword("media-source", "auto"))
            .Add(IppAttribute.Integer("media-top-margin", Margin))
            .Add(IppAttribute.Integer("media-bottom-margin", Margin))
            .Add(IppAttribute.Integer("media-left-margin", Margin))
            .Add(IppAttribute.Integer("media-right-margin", Margin));

    private static IppAttributeGroup Filter(IppAttributeGroup group, IReadOnlyCollection<string>? requested)
    {
        if (requested is null || requested.Count == 0 ||
            requested.Contains("all") || requested.Contains("printer-description"))
            return group;

        var filtered = new IppAttributeGroup(IppTag.PrinterAttributes);
        foreach (var a in group.Attributes)
            if (requested.Contains(a.Name))
                filtered.Add(a);
        return filtered;
    }

    private static string Uuid(string queueId)
    {
        // Deterministic UUIDv5-style id from the queue id (namespace fixed).
        byte[] hash = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("axoprint:" + queueId));
        Span<byte> b = hash.AsSpan(0, 16);
        b[6] = (byte)((b[6] & 0x0F) | 0x50);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return $"urn:uuid:{Convert.ToHexString(b[..4]).ToLowerInvariant()}-" +
               $"{Convert.ToHexString(b.Slice(4, 2)).ToLowerInvariant()}-" +
               $"{Convert.ToHexString(b.Slice(6, 2)).ToLowerInvariant()}-" +
               $"{Convert.ToHexString(b.Slice(8, 2)).ToLowerInvariant()}-" +
               $"{Convert.ToHexString(b.Slice(10, 6)).ToLowerInvariant()}";
    }
}
