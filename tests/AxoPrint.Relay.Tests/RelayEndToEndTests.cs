using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AxoPrint.Ipp;
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

    private static byte[] Body(IppRequest req, byte[]? doc = null)
    {
        using var ms = new MemoryStream();
        IppWriter.Write(ms, req);
        if (doc is not null) ms.Write(doc);
        return ms.ToArray();
    }

    private static IppRequest NewRequest(IppOperation op, string printerUri)
    {
        var req = new IppRequest(op) { Version = IppVersion.V1_1 };
        req.OperationGroup
            .Add(IppAttribute.Charset("attributes-charset", "utf-8"))
            .Add(IppAttribute.Language("attributes-natural-language", "en"))
            .Add(IppAttribute.Uri("printer-uri", printerUri));
        return req;
    }

    private async Task<IppResponse> PostIpp(HttpClient client, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        var resp = await client.PostAsync($"/ipp/{Token}/printers/{Queue}", content);
        resp.EnsureSuccessStatusCode();
        using var s = await resp.Content.ReadAsStreamAsync();
        return (IppResponse)IppReader.Read(s, asResponse: true);
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
    public async Task Unauthenticated_Ipp_Is_Forbidden()
    {
        var client = _factory.CreateClient();
        var body = Body(NewRequest(IppOperation.GetPrinterAttributes, "ipp://x/y"));
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        var resp = await client.PostAsync($"/ipp/wrong-token/printers/{Queue}", content);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetPrinterAttributes_Advertises_IppEverywhere()
    {
        await RegisterPrinter();
        var client = _factory.CreateClient();
        var printerUri = $"ipp://localhost/ipp/{Token}/printers/{Queue}";

        var res = await PostIpp(client, Body(NewRequest(IppOperation.GetPrinterAttributes, printerUri)));

        Assert.Equal(IppStatus.SuccessfulOk, res.Status);
        var pg = res.FirstGroup(IppTag.PrinterAttributes)!;
        Assert.Contains("ipp-everywhere", pg["ipp-features-supported"]!.AsStrings());
        Assert.Contains("application/pdf", pg["document-format-supported"]!.AsStrings());
        Assert.Equal("Office Laser", pg["printer-name"]!.AsString());
        // Duplex printer advertises two-sided.
        Assert.Contains("two-sided-long-edge", pg["sides-supported"]!.AsStrings());
        // media-col-database must decode as a 1setOf collections.
        Assert.True(pg["media-col-database"]!.Values.Count >= 2);
        // IPP Everywhere raster/URF capabilities required for driverless install.
        Assert.NotNull(pg["urf-supported"]);
        Assert.NotNull(pg["pwg-raster-document-type-supported"]);
        Assert.NotNull(pg["pwg-raster-document-resolution-supported"]);
        Assert.Contains("image/urf", pg["document-format-supported"]!.AsStrings());
        Assert.NotNull(pg["printer-device-id"]);
    }

    [Fact]
    public async Task RestPrint_Queues_Pdf_And_Agent_Pulls_It()
    {
        await RegisterPrinter();
        byte[] pdf = "%PDF-1.4 rest job"u8.ToArray();

        var content = new ByteArrayContent(pdf);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var resp = await Authed().PostAsync($"/api/print/{Queue}?jobName=Report&copies=3", content);
        resp.EnsureSuccessStatusCode();

        var envelope = await Authed().GetFromJsonAsync<JobEnvelope>("/api/agent/jobs/next?wait=5");
        Assert.NotNull(envelope);
        Assert.Equal(Queue, envelope!.QueueId);
        Assert.Equal("Report", envelope.JobName);
        Assert.Equal(3, envelope.Copies);

        var doc = await Authed().GetByteArrayAsync($"/api/agent/jobs/{envelope.JobId}/document");
        Assert.Equal(pdf, doc);
    }

    [Fact]
    public async Task PrintJob_Queues_Document_And_Agent_Pulls_It()
    {
        await RegisterPrinter();
        var client = _factory.CreateClient();
        var printerUri = $"ipp://localhost/ipp/{Token}/printers/{Queue}";

        byte[] pdf = "%PDF-1.4 fake document body"u8.ToArray();
        var req = NewRequest(IppOperation.PrintJob, printerUri);
        req.OperationGroup.Add(IppAttribute.NameW("job-name", "Test Page"));
        var jobGroup = req.GetOrAddGroup(IppTag.JobAttributes);
        jobGroup.Add(IppAttribute.Integer("copies", 2));

        var res = await PostIpp(client, Body(req, pdf));
        Assert.Equal(IppStatus.SuccessfulOk, res.Status);
        int jobId = res.FirstGroup(IppTag.JobAttributes)!["job-id"]!.AsInt();
        Assert.True(jobId > 0);

        // Agent pulls the job.
        var envelope = await Authed().GetFromJsonAsync<JobEnvelope>("/api/agent/jobs/next?wait=5");
        Assert.NotNull(envelope);
        Assert.Equal(jobId.ToString(), envelope!.JobId);
        Assert.Equal("Test Page", envelope.JobName);
        Assert.Equal(2, envelope.Copies);

        // Agent downloads the document.
        var doc = await Authed().GetByteArrayAsync($"/api/agent/jobs/{jobId}/document");
        Assert.Equal(pdf, doc);

        // Agent reports completion.
        var statusResp = await Authed().PostAsJsonAsync($"/api/agent/jobs/{jobId}/status",
            new JobStatusUpdate { State = JobState.Completed });
        statusResp.EnsureSuccessStatusCode();

        // Get-Job-Attributes now reports completed.
        var jobReq = NewRequest(IppOperation.GetJobAttributes, printerUri);
        jobReq.OperationGroup.Add(IppAttribute.Integer("job-id", jobId));
        var jobRes = await PostIpp(client, Body(jobReq));
        Assert.Equal((int)IppJobState.Completed,
            jobRes.FirstGroup(IppTag.JobAttributes)!["job-state"]!.AsInt());
    }
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
