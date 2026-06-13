namespace AxoPrint.Ipp;

/// <summary>resolution value (RFC 8010 §3.5.2): cross-feed x feed + units.</summary>
public readonly record struct IppResolution(int CrossFeed, int Feed, byte Units)
{
    public const byte DotsPerInch = 3;
    public const byte DotsPerCentimeter = 4;
    public static IppResolution Dpi(int dpi) => new(dpi, dpi, DotsPerInch);
    public override string ToString() => $"{CrossFeed}x{Feed}{(Units == DotsPerInch ? "dpi" : "dpcm")}";
}

/// <summary>rangeOfInteger value (RFC 8010 §3.5.2).</summary>
public readonly record struct IppRange(int Lower, int Upper)
{
    public override string ToString() => $"{Lower}-{Upper}";
}

/// <summary>A collection value (begCollection..endCollection, RFC 8010 §3.1.6).</summary>
public sealed class IppCollection
{
    public List<IppAttribute> Members { get; } = new();

    public IppCollection Add(IppAttribute member)
    {
        Members.Add(member);
        return this;
    }

    public IppAttribute? this[string name] =>
        Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// A single typed IPP value: the value-tag plus the decoded payload. The CLR
/// type of <see cref="Value"/> depends on the tag (int, bool, string,
/// <see cref="IppResolution"/>, <see cref="IppRange"/>, <see cref="DateTimeOffset"/>,
/// <see cref="IppCollection"/> or byte[] for octetString).
/// </summary>
public sealed class IppValue
{
    public IppTag Tag { get; }
    public object? Value { get; }

    public IppValue(IppTag tag, object? value)
    {
        Tag = tag;
        Value = value;
    }

    public int AsInt() => Convert.ToInt32(Value);
    public bool AsBool() => Convert.ToBoolean(Value);
    public string AsString() => Value?.ToString() ?? string.Empty;
    public IppCollection AsCollection() => (IppCollection)Value!;

    public static IppValue Integer(int v) => new(IppTag.Integer, v);
    public static IppValue Enum(int v) => new(IppTag.Enum, v);
    public static IppValue Boolean(bool v) => new(IppTag.Boolean, v);
    public static IppValue Keyword(string v) => new(IppTag.Keyword, v);
    public static IppValue Uri(string v) => new(IppTag.Uri, v);
    public static IppValue Charset(string v) => new(IppTag.Charset, v);
    public static IppValue NaturalLanguage(string v) => new(IppTag.NaturalLanguage, v);
    public static IppValue MimeMediaType(string v) => new(IppTag.MimeMediaType, v);
    public static IppValue TextW(string v) => new(IppTag.TextWithoutLanguage, v);
    public static IppValue NameW(string v) => new(IppTag.NameWithoutLanguage, v);
    public static IppValue Resolution(IppResolution v) => new(IppTag.Resolution, v);
    public static IppValue Range(int lower, int upper) => new(IppTag.RangeOfInteger, new IppRange(lower, upper));
    public static IppValue DateTime(DateTimeOffset v) => new(IppTag.DateTime, v);
    public static IppValue Collection(IppCollection v) => new(IppTag.BegCollection, v);

    public override string ToString() => Value switch
    {
        IppCollection c => "{" + string.Join(", ", c.Members.Select(m => m.ToString())) + "}",
        _ => Value?.ToString() ?? "(null)",
    };
}
