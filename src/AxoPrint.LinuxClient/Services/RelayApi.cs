using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.Shared;

namespace AxoPrint.LinuxClient.Services;

/// <summary>Reads the printer list from the relay's sender API.</summary>
public sealed class RelayApi(LinuxConfig config)
{
    public async Task<IReadOnlyList<PrinterListItem>> GetPrintersAsync(CancellationToken ct)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(config.RelayBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
        var items = await http.GetFromJsonAsync<List<PrinterListItem>>("api/printers", ct);
        return items ?? new List<PrinterListItem>();
    }
}
