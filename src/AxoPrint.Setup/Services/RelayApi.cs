using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.Shared;

namespace AxoPrint.Setup.Services;

/// <summary>Reads the printer list from the relay's sender API.</summary>
public sealed class RelayApi(SetupConfig config)
{
    private HttpClient Http() => new()
    {
        BaseAddress = new Uri(config.RelayBaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", config.Token) },
    };

    public async Task<IReadOnlyList<PrinterListItem>> GetPrintersAsync(CancellationToken ct)
    {
        using var http = Http();
        http.Timeout = TimeSpan.FromSeconds(20);
        var items = await http.GetFromJsonAsync<List<PrinterListItem>>("api/printers", ct);
        return items ?? new List<PrinterListItem>();
    }

    /// <summary>Uploads a spooled PDF to the relay as a print job for the queue.</summary>
    public async Task PrintAsync(string queueId, string filePath, CancellationToken ct)
    {
        using var http = Http();
        await using var fs = File.OpenRead(filePath);
        using var content = new StreamContent(fs);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        string jobName = Uri.EscapeDataString($"Print {DateTime.Now:HH:mm:ss}");
        var resp = await http.PostAsync($"api/print/{queueId}?jobName={jobName}", content, ct);
        resp.EnsureSuccessStatusCode();
    }
}
