using AxoPrint.Ipp;

// Usage: IppProbe <printer-url-https>
// Sends an IPP Get-Printer-Attributes request like the Windows IPP client does
// and reports exactly what the relay returns.

if (args.Length == 0)
{
    Console.WriteLine("Usage: IppProbe <https://host/ipp/<token>/printers/<queue>>");
    return;
}
string url = args[0];

string ippUri = url.Replace("https://", "ipps://").Replace("http://", "ipp://");

var req = new IppRequest(IppOperation.GetPrinterAttributes) { Version = IppVersion.V1_1 };
req.OperationGroup
    .Add(IppAttribute.Charset("attributes-charset", "utf-8"))
    .Add(IppAttribute.Language("attributes-natural-language", "en"))
    .Add(IppAttribute.Uri("printer-uri", ippUri))
    .Add(IppAttribute.Keyword("requested-attributes", "all"));

byte[] body = IppWriter.Encode(req);

Console.WriteLine($"POST {url}");
Console.WriteLine($"  printer-uri: {ippUri}");
Console.WriteLine($"  request bytes: {body.Length}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var content = new ByteArrayContent(body);
content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/ipp");

try
{
    var resp = await http.PostAsync(url, content);
    Console.WriteLine($"\nHTTP {(int)resp.StatusCode} {resp.StatusCode}");
    Console.WriteLine($"Content-Type: {resp.Content.Headers.ContentType}");
    byte[] respBytes = await resp.Content.ReadAsByteArrayAsync();
    Console.WriteLine($"Response bytes: {respBytes.Length}");

    if (resp.Content.Headers.ContentType?.MediaType == "application/ipp")
    {
        using var ms = new MemoryStream(respBytes);
        var ipp = (IppResponse)IppReader.Read(ms, asResponse: true);
        Console.WriteLine($"\nIPP version: {ipp.Version}");
        Console.WriteLine($"IPP status:  {ipp.Status}");
        var pg = ipp.FirstGroup(IppTag.PrinterAttributes);
        if (pg is null)
        {
            Console.WriteLine("  (no printer-attributes group!)");
        }
        else
        {
            string[] keys =
            {
                "printer-name", "printer-state", "printer-is-accepting-jobs",
                "ipp-versions-supported", "ipp-features-supported",
                "document-format-supported", "printer-uri-supported",
                "uri-security-supported", "media-default", "printer-uuid",
            };
            Console.WriteLine($"  printer-attributes count: {pg.Attributes.Count}");
            foreach (var k in keys)
                Console.WriteLine($"  {k,-28} = {(pg[k]?.ToString() ?? "(missing)")}");
        }
    }
    else
    {
        string text = System.Text.Encoding.UTF8.GetString(respBytes);
        Console.WriteLine("\nBody (non-IPP):");
        Console.WriteLine(text.Length > 800 ? text[..800] : text);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\nREQUEST FAILED: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException is not null)
        Console.WriteLine($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
}
