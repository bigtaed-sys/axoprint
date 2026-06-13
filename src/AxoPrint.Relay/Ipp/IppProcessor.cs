using AxoPrint.Ipp;
using AxoPrint.Relay.Stores;
using AxoPrint.Shared;

namespace AxoPrint.Relay.Ipp;

/// <summary>
/// Executes IPP operations against the printer registry and job store. The
/// request's attributes are already parsed; <paramref name="body"/> is
/// positioned at the document data (for Print-Job / Send-Document).
/// </summary>
public sealed class IppProcessor(PrinterRegistry registry, JobStore jobs, ILogger<IppProcessor> log)
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public async Task<IppResponse> ProcessAsync(
        IppRequest req, Stream body, string queueId, string printerUri, CancellationToken ct)
    {
        var printer = registry.Get(queueId);
        if (printer is null)
            return Error(req, IppStatus.ClientErrorNotFound, "Unknown printer queue.");

        try
        {
            return req.Operation switch
            {
                IppOperation.GetPrinterAttributes => GetPrinterAttributes(req, printer, printerUri),
                IppOperation.ValidateJob => Ok(req),
                IppOperation.IdentifyPrinter => Ok(req),
                IppOperation.PrintJob => await PrintJob(req, body, printer, printerUri, ct),
                IppOperation.CreateJob => CreateJob(req, printer, printerUri),
                IppOperation.SendDocument => await SendDocument(req, body, printer, printerUri, ct),
                IppOperation.GetJobAttributes => GetJobAttributes(req, printerUri),
                IppOperation.GetJobs => GetJobs(req, printer, printerUri),
                IppOperation.CancelJob => CancelJob(req),
                IppOperation.CloseJob => CloseJob(req, printerUri),
                _ => Error(req, IppStatus.ServerErrorOperationNotSupported,
                    $"Operation {req.Operation} not supported."),
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "IPP {Operation} failed for {Queue}", req.Operation, queueId);
            return Error(req, IppStatus.ServerErrorInternalError, ex.Message);
        }
    }

    private IppResponse GetPrinterAttributes(IppRequest req, PrinterDescriptor printer, string printerUri)
    {
        var res = Ok(req);
        var requested = req.OperationGroup["requested-attributes"]?.AsStrings().ToHashSet();
        var state = registry.AgentOnline ? IppPrinterState.Idle : IppPrinterState.Stopped;
        var group = PrinterAttributes.Build(
            printer, printerUri, state,
            acceptingJobs: true,
            queuedJobCount: jobs.PendingCount(printer.QueueId),
            upTimeSeconds: (int)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds,
            requested);
        res.Groups.Add(group);
        return res;
    }

    private async Task<IppResponse> PrintJob(
        IppRequest req, Stream body, PrinterDescriptor printer, string printerUri, CancellationToken ct)
    {
        var job = CreateRecord(req, printer);
        long bytes = await SaveDocument(job.Id, body, ct);
        jobs.Commit(job, bytes);
        log.LogInformation("Job {Id} queued for {Queue} ({Bytes} bytes)", job.Id, printer.QueueId, bytes);
        return JobResponse(req, job, printerUri);
    }

    private IppResponse CreateJob(IppRequest req, PrinterDescriptor printer, string printerUri)
    {
        var job = CreateRecord(req, printer);
        return JobResponse(req, job, printerUri);
    }

    private async Task<IppResponse> SendDocument(
        IppRequest req, Stream body, PrinterDescriptor printer, string printerUri, CancellationToken ct)
    {
        int jobId = req.OperationGroup["job-id"]?.AsInt() ?? 0;
        var job = jobs.Get(jobId);
        if (job is null || job.QueueId != printer.QueueId)
            return Error(req, IppStatus.ClientErrorNotFound, "Unknown job-id.");

        bool last = req.OperationGroup["last-document"]?.AsBool() ?? true;
        long bytes = await SaveDocument(job.Id, body, ct);
        if (last && bytes > 0)
            jobs.Commit(job, bytes);
        return JobResponse(req, job, printerUri);
    }

    private IppResponse GetJobAttributes(IppRequest req, string printerUri)
    {
        int jobId = req.OperationGroup["job-id"]?.AsInt() ?? 0;
        var job = jobs.Get(jobId);
        if (job is null)
            return Error(req, IppStatus.ClientErrorNotFound, "Unknown job-id.");
        return JobResponse(req, job, printerUri);
    }

    private IppResponse GetJobs(IppRequest req, PrinterDescriptor printer, string printerUri)
    {
        var res = Ok(req);
        string which = req.OperationGroup["which-jobs"]?.AsString() ?? "not-completed";
        bool completed = which == "completed";
        var list = jobs.ForQueue(printer.QueueId, j =>
            completed
                ? j.State is JobState.Completed or JobState.Canceled or JobState.Aborted
                : j.State is JobState.Pending or JobState.Processing);
        foreach (var job in list)
            res.Groups.Add(JobGroup(job, printerUri));
        return res;
    }

    private IppResponse CancelJob(IppRequest req)
    {
        int jobId = req.OperationGroup["job-id"]?.AsInt() ?? 0;
        var job = jobs.Get(jobId);
        if (job is null)
            return Error(req, IppStatus.ClientErrorNotFound, "Unknown job-id.");
        if (job.State is JobState.Pending or JobState.Processing)
            jobs.UpdateState(jobId, JobState.Canceled, "Canceled by client.");
        return Ok(req);
    }

    private IppResponse CloseJob(IppRequest req, string printerUri)
    {
        int jobId = req.OperationGroup["job-id"]?.AsInt() ?? 0;
        var job = jobs.Get(jobId);
        return job is null ? Ok(req) : JobResponse(req, job, printerUri);
    }

    // ---- helpers --------------------------------------------------------

    private JobRecord CreateRecord(IppRequest req, PrinterDescriptor printer)
    {
        var op = req.OperationGroup;
        return jobs.Create(
            printer.QueueId,
            jobName: op["job-name"]?.AsString() ?? "Document",
            userName: op["requesting-user-name"]?.AsString() ?? "",
            format: op["document-format"]?.AsString() ?? "application/pdf",
            copies: req.FirstGroup(IppTag.JobAttributes)?["copies"]?.AsInt() ?? 1,
            duplex: (req.FirstGroup(IppTag.JobAttributes)?["sides"]?.AsString() ?? "one-sided") != "one-sided",
            color: (req.FirstGroup(IppTag.JobAttributes)?["print-color-mode"]?.AsString() ?? "color") != "monochrome",
            media: req.FirstGroup(IppTag.JobAttributes)?["media"]?.AsString() ?? "");
    }

    private async Task<long> SaveDocument(int jobId, Stream body, CancellationToken ct)
    {
        await using var file = File.Create(jobs.DocumentPath(jobId));
        await body.CopyToAsync(file, ct);
        return file.Length;
    }

    private static IppResponse JobResponse(IppRequest req, JobRecord job, string printerUri)
    {
        var res = Ok(req);
        res.Groups.Add(JobGroup(job, printerUri));
        return res;
    }

    private static IppAttributeGroup JobGroup(JobRecord job, string printerUri)
    {
        var g = new IppAttributeGroup(IppTag.JobAttributes);
        g.Add(new IppAttribute("job-uri", IppValue.Uri($"{printerUri}/{job.Id}")));
        g.Add(IppAttribute.Integer("job-id", job.Id));
        g.Add(new IppAttribute("job-printer-uri", IppValue.Uri(printerUri)));
        g.Add(IppAttribute.NameW("job-name", job.JobName));
        g.Add(IppAttribute.Enum("job-state", (int)job.State));
        g.Add(IppAttribute.Keyword("job-state-reasons",
            job.State == JobState.Pending ? "none"
            : job.State == JobState.Processing ? "job-printing"
            : job.State == JobState.Completed ? "job-completed-successfully"
            : "job-canceled-by-user"));
        return g;
    }

    private static IppResponse Ok(IppRequest req) => BaseResponse(req, IppStatus.SuccessfulOk);

    private static IppResponse Error(IppRequest req, IppStatus status, string message)
    {
        var res = BaseResponse(req, status);
        res.OperationGroup.Add(IppAttribute.TextW("status-message", message));
        return res;
    }

    private static IppResponse BaseResponse(IppRequest req, IppStatus status)
    {
        var res = new IppResponse(status, req.RequestId) { Version = req.Version };
        res.OperationGroup
            .Add(IppAttribute.Charset("attributes-charset", "utf-8"))
            .Add(IppAttribute.Language("attributes-natural-language", "en"));
        return res;
    }
}
