using System.Collections;

namespace AxoPrint.Ipp;

/// <summary>
/// An IPP attribute: a name plus one or more values that share a value-tag
/// kind. (1setOf attributes carry additional values; RFC 8010 §3.1.5.)
/// </summary>
public sealed class IppAttribute
{
    public string Name { get; }
    public List<IppValue> Values { get; } = new();

    public IppAttribute(string name, params IppValue[] values)
    {
        Name = name;
        Values.AddRange(values);
    }

    public IppTag Tag => Values.Count > 0 ? Values[0].Tag : IppTag.NoValue;
    public IppValue First => Values[0];

    public int AsInt() => First.AsInt();
    public bool AsBool() => First.AsBool();
    public string AsString() => First.AsString();
    public IEnumerable<string> AsStrings() => Values.Select(v => v.AsString());

    public override string ToString() =>
        $"{Name}={string.Join("|", Values.Select(v => v.ToString()))}";

    // Convenience factories ------------------------------------------------
    public static IppAttribute Charset(string name, string v) => new(name, IppValue.Charset(v));
    public static IppAttribute Language(string name, string v) => new(name, IppValue.NaturalLanguage(v));
    public static IppAttribute Uri(string name, string v) => new(name, IppValue.Uri(v));
    public static IppAttribute Keyword(string name, params string[] v) =>
        new(name, v.Select(IppValue.Keyword).ToArray());
    public static IppAttribute Integer(string name, params int[] v) =>
        new(name, v.Select(IppValue.Integer).ToArray());
    public static IppAttribute Enum(string name, int v) => new(name, IppValue.Enum(v));
    public static IppAttribute Boolean(string name, bool v) => new(name, IppValue.Boolean(v));
    public static IppAttribute TextW(string name, string v) => new(name, IppValue.TextW(v));
    public static IppAttribute NameW(string name, string v) => new(name, IppValue.NameW(v));
    public static IppAttribute Mime(string name, params string[] v) =>
        new(name, v.Select(IppValue.MimeMediaType).ToArray());
}

/// <summary>
/// A delimited group of attributes (operation-attributes, job-attributes,
/// printer-attributes, ...). Keeps insertion order, which IPP requires for
/// the first three attributes of the operation group.
/// </summary>
public sealed class IppAttributeGroup : IEnumerable<IppAttribute>
{
    public IppTag Tag { get; }
    public List<IppAttribute> Attributes { get; } = new();

    public IppAttributeGroup(IppTag tag) => Tag = tag;

    public IppAttributeGroup Add(IppAttribute attr)
    {
        Attributes.Add(attr);
        return this;
    }

    public IppAttribute? this[string name] =>
        Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    public IEnumerator<IppAttribute> GetEnumerator() => Attributes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
