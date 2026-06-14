using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AxoPrint.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AxoPrint.Relay.Tests;

public class RelayEndToEndTests : IClassFixture<RelayFactory>
{
    private const string Token = "test-secret-token";
    private const string Queue = "office-laser";
    private readonly RelayFactory _factory;

    public RelayEndToEndTests(RelayFactory factory) => _factory = factory;

    private HttpClient Authed()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return c;
    }

    private async Task RegisterPrinter()
    {
        var req = new RegisterRequest
        {
            AgentId = "test-agent",
            Printers = new[]
            {
                new PrinterDescriptor
                {
                    QueueId = Queue, DisplayName = "Office Laser",
                    MakeAndModel = "AxoPrint Test", LocalName = "Office Laser", Duplex = true,
                },
            },
        };
        var resp = await Authed().PostAsJsonAsync("/api/agent/register", req);
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unauthenticated_Print_Is_Unauthorized()
    {
        var client = _factory.CreateClient();
        var content = new ByteArrayContent("%PDF"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var resp = await client.PostAsync($"/api/print/{Queue}", content);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Printers_Are_Listed_After_Registration()
    {
        await RegisterPrinter();
        var items = await Authed().GetFromJsonAsync<List<PrinterListItem>>("/api/printers");
        Assert.NotNull(items);
        Assert.Contains(items!, i => i.QueueId == Queue && i.DisplayName == "Office Laser");
    }

    [Fact]
    public async Task RestPrint_Queues_Pdf_And_Agent_Pulls_It()
    {
        await RegisterPrinter();
        byte[] pdf = "%PDF-1.4 rest job"u8.ToArray();

        var content = new ByteArrayContent(pdf);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var resp = await Authed().PostAsync($"/api/print/{Queue}?jobName=Report&copies=3&duplex=true&color=false", content);
        resp.EnsureSuccessStatusCode();

        var envelope = await Authed().GetFromJsonAsync<JobEnvelope>("/api/agent/jobs/next?wait=5");
        Assert.NotNull(envelope);
        Assert.Equal(Queue, envelope!.QueueId);
        Assert.Equal("Report", envelope.JobName);
        Assert.Equal(3, envelope.Copies);
        Assert.True(envelope.Duplex);
        Assert.False(envelope.Color);

        var doc = await Authed().GetByteArrayAsync($"/api/agent/jobs/{envelope.JobId}/document");
        Assert.Equal(pdf, doc);
    }

    [Fact]
    public async Task JobStatus_Reflects_Agent_Updates()
    {
        await RegisterPrinter();
        var content = new ByteArrayContent("%PDF"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var resp = await Authed().PostAsync($"/api/print/{Queue}", content);
        int jobId = (await resp.Content.ReadFromJsonAsync<PrintAck>())!.JobId;

        // Agent pulls (-> Processing) then reports completion.
        await Authed().GetAsync("/api/agent/jobs/next?wait=5");
        var upd = await Authed().PostAsJsonAsync($"/api/agent/jobs/{jobId}/status",
            new JobStatusUpdate { State = JobState.Completed });
        upd.EnsureSuccessStatusCode();

        var status = await Authed().GetFromJsonAsync<JobStatusView>($"/api/jobs/{jobId}/status");
        Assert.Equal("Completed", status!.State);
    }

    private sealed record PrintAck(int JobId);
    private sealed record JobStatusView(string State, string? Message);
}

public class EmptyDataDirTests
{
    // Regression: an empty (not null) Axo:DataDir from appsettings used to crash
    // store construction with Path "". The relay must fall back to ContentRoot/data.
    [Fact]
    public async Task EmptyDataDir_FallsBackAndServes()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "axoprint-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.UseContentRoot(contentRoot);
                    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Axo:Token"] = "t",
                        ["Axo:DataDir"] = "",
                    }));
                });
            var client = factory.CreateClient();
            var resp = await client.GetAsync("/");
            resp.EnsureSuccessStatusCode();
            Assert.True(Directory.Exists(Path.Combine(contentRoot, "data")));
        }
        finally
        {
            try { Directory.Delete(contentRoot, true); } catch { }
        }
    }
}

public sealed class RelayFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "axoprint-test-" + Guid.NewGuid().ToString("N"));

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Axo:Token"] = "test-secret-token",
            ["Axo:DataDir"] = _dataDir,
        }));
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true); } catch { }
    }
}
