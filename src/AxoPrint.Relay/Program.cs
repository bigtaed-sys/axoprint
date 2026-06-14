using AxoPrint.Relay;
using AxoPrint.Relay.Stores;
using AxoPrint.Shared;

var builder = WebApplication.CreateBuilder(args);

// Integrate with systemd (Type=notify, journald logging) when run as a service.
builder.Host.UseSystemd();

builder.Services.AddSingleton<TokenAuth>();
builder.Services.AddSingleton<PrinterRegistry>();
builder.Services.AddSingleton<JobStore>();

// Print documents can be large; allow big request bodies.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 512L * 1024 * 1024);

var app = builder.Build();

// ----- Status page -------------------------------------------------------
app.MapGet("/", (PrinterRegistry reg) =>
{
    var lines = reg.All.Select(p => $"  • {p.DisplayName} (queue: {p.QueueId})");
    return Results.Text(
        $"""
        AxoPrint relay
        Agent: {(reg.AgentOnline ? "online" : "offline")} (last seen {reg.LastSeen:u})
        Printers:
        {string.Join('\n', lines)}
        """, "text/plain");
});

// ----- Sender print API: submit a PDF for a queue (used by the client) ---
app.MapPost("/api/print/{queueId}", async (
    string queueId, HttpContext ctx, TokenAuth auth, PrinterRegistry reg, JobStore jobs,
    string? jobName, int? copies, bool? duplex, bool? color, CancellationToken ct) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();
    if (reg.Get(queueId) is null)
        return Results.NotFound(new { error = "unknown queue" });

    var job = jobs.Create(queueId, jobName ?? "Document", "", "application/pdf",
        Math.Clamp(copies ?? 1, 1, 999), duplex: duplex ?? false, color: color ?? true, media: "");

    await using (var f = File.Create(jobs.DocumentPath(job.Id)))
        await ctx.Request.Body.CopyToAsync(f, ct);
    jobs.Commit(job, new FileInfo(jobs.DocumentPath(job.Id)).Length);

    return Results.Ok(new { jobId = job.Id });
});

// ----- Sender API: poll a job's status (client shows printed/failed) -----
app.MapGet("/api/jobs/{id:int}/status", (int id, HttpContext ctx, TokenAuth auth, JobStore jobs) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();
    var job = jobs.Get(id);
    if (job is null)
        return Results.NotFound();
    return Results.Ok(new { state = job.State.ToString(), message = job.StateMessage });
});

// ----- Sender setup API: list printers to add to Windows -----------------
app.MapGet("/api/printers", (HttpContext ctx, TokenAuth auth, PrinterRegistry reg, IConfiguration config) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();

    var token = ctx.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
    var items = reg.All.Select(p => new PrinterListItem
    {
        QueueId = p.QueueId,
        DisplayName = p.DisplayName,
        Url = BuildClientUrl(ctx, config, token, p.QueueId),
        AgentOnline = reg.AgentOnline,
    });
    return Results.Ok(items);
});

// ----- Agent API (bearer token) -----------------------------------------
var agent = app.MapGroup("/api/agent");

agent.MapPost("/register", (RegisterRequest req, HttpContext ctx, TokenAuth auth, PrinterRegistry reg) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();
    reg.Register(req);
    return Results.Ok(new { ok = true, printers = req.Printers.Count });
});

agent.MapGet("/jobs/next", async (
    HttpContext ctx, TokenAuth auth, PrinterRegistry reg, JobStore jobs,
    int? wait, CancellationToken ct) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();
    reg.Heartbeat();

    var owned = reg.QueueIds;
    var timeout = TimeSpan.FromSeconds(Math.Clamp(wait ?? 25, 1, 55));
    var job = await jobs.WaitNextAsync(owned, timeout, ct);
    if (job is null)
        return Results.NoContent();

    return Results.Ok(new JobEnvelope
    {
        JobId = job.Id.ToString(),
        QueueId = job.QueueId,
        DocumentFormat = job.DocumentFormat,
        JobName = job.JobName,
        UserName = job.UserName,
        Copies = job.Copies,
        Duplex = job.Duplex,
        Color = job.Color,
        Media = job.Media,
        DocumentBytes = job.DocumentBytes,
        CreatedAt = job.CreatedAt,
    });
});

agent.MapGet("/jobs/{id:int}/document", (int id, HttpContext ctx, TokenAuth auth, JobStore jobs) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();
    var job = jobs.Get(id);
    var path = jobs.DocumentPath(id);
    if (job is null || !File.Exists(path))
        return Results.NotFound();
    return Results.File(path, job.DocumentFormat, enableRangeProcessing: true);
});

agent.MapPost("/jobs/{id:int}/status", (
    int id, JobStatusUpdate update, HttpContext ctx, TokenAuth auth, JobStore jobs) =>
{
    if (!auth.IsValidBearer(ctx.Request.Headers.Authorization))
        return Results.Unauthorized();
    if (jobs.Get(id) is null)
        return Results.NotFound();
    jobs.UpdateState(id, update.State, update.Message);
    return Results.Ok();
});

app.Run();

// Builds the relay base URL shown to the client for reference.
static string BuildClientUrl(HttpContext ctx, IConfiguration config, string token, string queue)
{
    string? configured = config["Axo:BaseUri"];
    string scheme, host;
    if (!string.IsNullOrWhiteSpace(configured))
    {
        var baseUri = new Uri(configured);
        scheme = baseUri.Scheme;
        host = baseUri.Authority;
    }
    else
    {
        scheme = ctx.Request.Scheme;
        host = ctx.Request.Host.Value ?? "localhost";
    }
    return $"{scheme}://{host}/ipp/{token}/printers/{queue}";
}

public partial class Program { }
