using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AxoPrint.Shared;

namespace AxoPrint.Agent.Services;

/// <summary>Talks to the relay's agent REST API over HTTPS with a bearer token.</summary>
public sealed class RelayClient
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;

    public RelayClient(AgentConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.RelayBaseUrl.TrimEnd('/') + "/"),
            // jobs/next is a long-poll; allow it to exceed the wait window.
            Timeout = TimeSpan.FromSeconds(config.PollSeconds + 30),
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config.Token);
    }

    public async Task RegisterAsync(IReadOnlyList<PrinterDescriptor> printers, CancellationToken ct)
    {
        var req = new RegisterRequest
        {
            AgentId = _config.AgentId,
            AgentVersion = "1.0",
            Printers = printers,
        };
        var resp = await _http.PostAsJsonAsync("api/agent/register", req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Long-polls for the next job; returns null on timeout (no work).</summary>
    public async Task<JobEnvelope?> PullJobAsync(CancellationToken ct)
    {
        var resp = await _http.GetAsync($"api/agent/jobs/next?wait={_config.PollSeconds}", ct);
        if (resp.StatusCode == HttpStatusCode.NoContent)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JobEnvelope>(ct);
    }

    public async Task DownloadDocumentAsync(string jobId, string destinationPath, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(
            $"api/agent/jobs/{jobId}/document", HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destinationPath);
        await src.CopyToAsync(dst, ct);
    }

    public async Task ReportStatusAsync(string jobId, JobState state, string? message, CancellationToken ct)
    {
        var update = new JobStatusUpdate { State = state, Message = message };
        var resp = await _http.PostAsJsonAsync($"api/agent/jobs/{jobId}/status", update, ct);
        resp.EnsureSuccessStatusCode();
    }
}
