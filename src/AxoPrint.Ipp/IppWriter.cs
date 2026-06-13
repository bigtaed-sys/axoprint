using System.Buffers.Binary;
using System.Text;

namespace AxoPrint.Ipp;

/// <summary>
/// Serializes an <see cref="IppMessage"/> to the IPP binary form (RFC 8010).
/// All multi-byte integers are big-endian; strings are UTF-8.
/// </summary>
public static class IppWriter
{
    public static byte[] Encode(IppMessage message)
    {
        using var ms = new MemoryStream();
        Write(ms, message);
        return ms.ToArray();
    }

    public static void Write(Stream stream, IppMessage message)
    {
        Span<byte> header = stackalloc byte[8];
        header[0] = message.Version.Major;
        header[1] = message.Version.Minor;
        short opOrStatus = message switch
        {
            IppRequest r => (short)r.Operation,
            IppResponse r => (short)r.Status,
            _ => throw new ArgumentException("Unknown message type", nameof(message)),
        };
        BinaryPrimitives.WriteInt16BigEndian(header[2..], opOrStatus);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], message.RequestId);
        stream.Write(header);

        foreach (var group in message.Groups)
        {
            stream.WriteByte((byte)group.Tag);
            foreach (var attr in group.Attributes)
                WriteAttribute(stream, attr);
        }

        stream.WriteByte((byte)IppTag.EndOfAttributes);
    }

    private static void WriteAttribute(Stream s, IppAttribute attr)
    {
        for (int i = 0; i < attr.Values.Count; i++)
        {
            string name = i == 0 ? attr.Name : string.Empty;
            WriteValue(s, name, attr.Values[i]);
        }
    }

    private static void WriteValue(Stream s, string name, IppValue value)
    {
        if (value.Tag == IppTag.BegCollection)
        {
            WriteCollection(s, name, value.AsCollection());
            return;
        }

        WriteRaw(s, value.Tag, name, EncodeScalar(value));
    }

    private static void WriteCollection(Stream s, string name, IppCollection collection)
    {
        // begCollection: named, empty value.
        WriteRaw(s, IppTag.BegCollection, name, ReadOnlySpan<byte>.Empty);

        foreach (var member in collection.Members)
        {
            // memberAttrName: no name, value = member's attribute name.
            WriteRaw(s, IppTag.MemberAttrName, string.Empty, Encoding.UTF8.GetBytes(member.Name));
            // Member values are written nameless (recurses for nested collections).
            foreach (var v in member.Values)
                WriteValue(s, string.Empty, v);
        }

        WriteRaw(s, IppTag.EndCollection, string.Empty, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteRaw(Stream s, IppTag tag, string name, ReadOnlySpan<byte> value)
    {
        s.WriteByte((byte)tag);

        Span<byte> len = stackalloc byte[2];
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)nameBytes.Length);
        s.Write(len);
        s.Write(nameBytes);

        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)value.Length);
        s.Write(len);
        s.Write(value);
    }

    private static byte[] EncodeScalar(IppValue v)
    {
        switch (v.Tag)
        {
            case IppTag.Integer:
            case IppTag.Enum:
            {
                var b = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(b, v.AsInt());
                return b;
            }
            case IppTag.Boolean:
                return new[] { (byte)(v.AsBool() ? 1 : 0) };
            case IppTag.RangeOfInteger:
            {
                var r = (IppRange)v.Value!;
                var b = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0), r.Lower);
                BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), r.Upper);
                return b;
            }
            case IppTag.Resolution:
            {
                var r = (IppResolution)v.Value!;
                var b = new byte[9];
                BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0), r.CrossFeed);
                BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), r.Feed);
                b[8] = r.Units;
                return b;
            }
            case IppTag.DateTime:
                return EncodeDateTime((DateTimeOffset)v.Value!);
            case IppTag.OctetStringUnspecified:
                return v.Value as byte[] ?? Array.Empty<byte>();
            case IppTag.NoValue:
            case IppTag.Unknown:
            case IppTag.Unsupported:
                return Array.Empty<byte>();
            default:
                // All character-string syntaxes.
                return Encoding.UTF8.GetBytes(v.AsString());
        }
    }

    // RFC 2579 DateAndTime, 11 octets.
    private static byte[] EncodeDateTime(DateTimeOffset dt)
    {
        var b = new byte[11];
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0), (ushort)dt.Year);
        b[2] = (byte)dt.Month;
        b[3] = (byte)dt.Day;
        b[4] = (byte)dt.Hour;
        b[5] = (byte)dt.Minute;
        b[6] = (byte)dt.Second;
        b[7] = (byte)(dt.Millisecond / 100);
        var off = dt.Offset;
        b[8] = (byte)(off.Ticks >= 0 ? '+' : '-');
        b[9] = (byte)Math.Abs(off.Hours);
        b[10] = (byte)Math.Abs(off.Minutes);
        return b;
    }
}
