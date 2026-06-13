namespace AxoPrint.Ipp;

/// <summary>IPP operation codes (RFC 8011 §4.4.15 and PWG extensions).</summary>
public enum IppOperation : short
{
    PrintJob = 0x0002,
    PrintUri = 0x0003,
    ValidateJob = 0x0004,
    CreateJob = 0x0005,
    SendDocument = 0x0006,
    SendUri = 0x0007,
    CancelJob = 0x0008,
    GetJobAttributes = 0x0009,
    GetJobs = 0x000A,
    GetPrinterAttributes = 0x000B,
    HoldJob = 0x000C,
    ReleaseJob = 0x000D,
    PausePrinter = 0x0010,
    ResumePrinter = 0x0011,
    PurgeJobs = 0x0012,
    CloseJob = 0x003B,     // PWG 5100.11
    IdentifyPrinter = 0x003C,
}

/// <summary>IPP status codes (RFC 8011 §4.1.6 + extensions).</summary>
public enum IppStatus : short
{
    SuccessfulOk = 0x0000,
    SuccessfulOkIgnoredOrSubstituted = 0x0001,
    SuccessfulOkConflicting = 0x0002,

    ClientErrorBadRequest = 0x0400,
    ClientErrorForbidden = 0x0401,
    ClientErrorNotAuthenticated = 0x0402,
    ClientErrorNotAuthorized = 0x0403,
    ClientErrorNotPossible = 0x0404,
    ClientErrorTimeout = 0x0405,
    ClientErrorNotFound = 0x0406,
    ClientErrorGone = 0x0407,
    ClientErrorRequestEntityTooLarge = 0x0408,
    ClientErrorRequestValueTooLong = 0x0409,
    ClientErrorDocumentFormatNotSupported = 0x040A,
    ClientErrorAttributesOrValuesNotSupported = 0x040B,
    ClientErrorUriSchemeNotSupported = 0x040C,
    ClientErrorCharsetNotSupported = 0x040D,
    ClientErrorConflictingAttributes = 0x040E,
    ClientErrorCompressionNotSupported = 0x040F,
    ClientErrorCompressionError = 0x0410,
    ClientErrorDocumentFormatError = 0x0411,
    ClientErrorDocumentAccessError = 0x0412,

    ServerErrorInternalError = 0x0500,
    ServerErrorOperationNotSupported = 0x0501,
    ServerErrorServiceUnavailable = 0x0502,
    ServerErrorVersionNotSupported = 0x0503,
    ServerErrorDeviceError = 0x0504,
    ServerErrorTemporaryError = 0x0505,
    ServerErrorNotAcceptingJobs = 0x0506,
    ServerErrorBusy = 0x0507,
    ServerErrorJobCanceled = 0x0508,
}

/// <summary>printer-state values (RFC 8011 §5.4.11).</summary>
public enum IppPrinterState
{
    Idle = 3,
    Processing = 4,
    Stopped = 5,
}

/// <summary>job-state values (RFC 8011 §5.3.7).</summary>
public enum IppJobState
{
    Pending = 3,
    PendingHeld = 4,
    Processing = 5,
    ProcessingStopped = 6,
    Canceled = 7,
    Aborted = 8,
    Completed = 9,
}

public readonly record struct IppVersion(byte Major, byte Minor)
{
    public static readonly IppVersion V1_1 = new(1, 1);
    public static readonly IppVersion V2_0 = new(2, 0);
    public override string ToString() => $"{Major}.{Minor}";
}
