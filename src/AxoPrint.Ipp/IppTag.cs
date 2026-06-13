namespace AxoPrint.Ipp;

/// <summary>
/// IPP tags as defined in RFC 8010 §3.5.1. Two kinds: delimiter/group tags
/// (0x00-0x05) that separate attribute groups, and value tags (>= 0x10) that
/// describe the syntax of an attribute value.
/// </summary>
public enum IppTag : byte
{
    // Delimiter tags
    Reserved = 0x00,
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    EndOfAttributes = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,

    // "Out-of-band" value tags (RFC 8010 §3.8)
    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,

    // Integer value tags
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,

    // Octet-string value tags
    OctetStringUnspecified = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BegCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,

    // Character-string value tags
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A,
}

public static class IppTagExtensions
{
    public static bool IsDelimiter(this IppTag tag) => (byte)tag <= 0x05;
}
