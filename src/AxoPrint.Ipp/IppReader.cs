using System.Buffers.Binary;
using System.Text;

namespace AxoPrint.Ipp;

/// <summary>
/// Parses the IPP binary form (RFC 8010) into an <see cref="IppMessage"/>.
/// Attributes are read first; the stream is then positioned at the start of
/// the document data (if any), so large payloads need not be buffered.
/// </summary>
public static class IppReader
{
    /// <summary>Reads the message header + attribute groups. Leaves <paramref name="stream"/>
    /// positioned at the first byte of the document data.</summary>
    public static IppMessage Read(Stream stream, bool asResponse = false)
    {
        Span<byte> header = stackalloc byte[8];
        ReadExact(stream, header);

        var version = new IppVersion(header[0], header[1]);
        short opOrStatus = BinaryPrimitives.ReadInt16BigEndian(header[2..]);
        int requestId = BinaryPrimitives.ReadInt32BigEndian(header[4..]);

        var entries = ReadEntries(stream);

        IppMessage msg = asResponse
            ? new IppResponse { Status = (IppStatus)opOrStatus }
            : new IppRequest { Operation = (IppOperation)opOrStatus };
        msg.Version = version;
        msg.RequestId = requestId;

        Assemble(entries, msg);
        return msg;
    }

    private readonly record struct RawEntry(IppTag Tag, bool IsDelimiter, string Name, byte[] Value);

    private static List<RawEntry> ReadEntries(Stream s)
    {
        var entries = new List<RawEntry>();
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0)
                throw new EndOfStreamException("Unexpected end of IPP stream.");
            var tag = (IppTag)(byte)b;

            if (tag == IppTag.EndOfAttributes)
                break;

            if (tag.IsDelimiter())
            {
                entries.Add(new RawEntry(tag, true, string.Empty, Array.Empty<byte>()));
                continue;
            }

            string name = Encoding.UTF8.GetString(ReadLengthPrefixed(s));
            byte[] value = ReadLengthPrefixed(s);
            entries.Add(new RawEntry(tag, false, name, value));
        }
        return entries;
    }

    private static byte[] ReadLengthPrefixed(Stream s)
    {
        Span<byte> len = stackalloc byte[2];
        ReadExact(s, len);
        int n = BinaryPrimitives.ReadUInt16BigEndian(len);
        if (n == 0)
            return Array.Empty<byte>();
        var buf = new byte[n];
        ReadExact(s, buf);
        return buf;
    }

    private static void Assemble(List<RawEntry> entries, IppMessage msg)
    {
        IppAttributeGroup? group = null;
        IppAttribute? last = null;
        int i = 0;

        while (i < entries.Count)
        {
            var e = entries[i];
            if (e.IsDelimiter)
            {
                group = new IppAttributeGroup(e.Tag);
                msg.Groups.Add(group);
                last = null;
                i++;
                continue;
            }

            if (group is null)
                throw new InvalidDataException("Attribute before any group delimiter.");

            IppValue value;
            if (e.Tag == IppTag.BegCollection)
            {
                i++; // consume begCollection
                value = IppValue.Collection(ParseCollection(entries, ref i));
            }
            else
            {
                value = DecodeValue(e.Tag, e.Value);
                i++;
            }

            if (e.Name.Length > 0)
            {
                last = new IppAttribute(e.Name);
                last.Values.Add(value);
                group.Add(last);
            }
            else
            {
                if (last is null)
                    throw new InvalidDataException("Additional value without an attribute.");
                last.Values.Add(value);
            }
        }
    }

    private static IppCollection ParseCollection(List<RawEntry> entries, ref int i)
    {
        var coll = new IppCollection();
        while (i < entries.Count)
        {
            var e = entries[i];
            if (e.Tag == IppTag.EndCollection)
            {
                i++;
                return coll;
            }

            if (e.Tag != IppTag.MemberAttrName)
            {
                i++; // skip stray entry defensively
                continue;
            }

            string memberName = Encoding.UTF8.GetString(e.Value);
            i++; // consume memberAttrName
            var member = new IppAttribute(memberName);

            while (i < entries.Count &&
                   entries[i].Tag != IppTag.MemberAttrName &&
                   entries[i].Tag != IppTag.EndCollection)
            {
                var ve = entries[i];
                if (ve.Tag == IppTag.BegCollection)
                {
                    i++; // consume nested begCollection
                    member.Values.Add(IppValue.Collection(ParseCollection(entries, ref i)));
                }
                else
                {
                    member.Values.Add(DecodeValue(ve.Tag, ve.Value));
                    i++;
                }
            }

            coll.Add(member);
        }
        throw new InvalidDataException("Collection not terminated by endCollection.");
    }

    private static IppValue DecodeValue(IppTag tag, byte[] data)
    {
        switch (tag)
        {
            case IppTag.Integer:
            case IppTag.Enum:
                return new IppValue(tag, BinaryPrimitives.ReadInt32BigEndian(data));
            case IppTag.Boolean:
                return new IppValue(tag, data.Length > 0 && data[0] != 0);
            case IppTag.RangeOfInteger:
                return new IppValue(tag, new IppRange(
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0)),
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4))));
            case IppTag.Resolution:
                return new IppValue(tag, new IppResolution(
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0)),
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)),
                    data[8]));
            case IppTag.DateTime:
                return new IppValue(tag, DecodeDateTime(data));
            case IppTag.OctetStringUnspecified:
                return new IppValue(tag, data);
            case IppTag.NoValue:
            case IppTag.Unknown:
            case IppTag.Unsupported:
                return new IppValue(tag, null);
            default:
                return new IppValue(tag, Encoding.UTF8.GetString(data));
        }
    }

    private static DateTimeOffset DecodeDateTime(byte[] b)
    {
        int year = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(0));
        int sign = b[8] == (byte)'-' ? -1 : 1;
        var offset = new TimeSpan(sign * b[9], sign * b[10], 0);
        return new DateTimeOffset(year, b[2], b[3], b[4], b[5], b[6], b[7] * 100, offset);
    }

    private static void ReadExact(Stream s, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = s.Read(buffer[read..]);
            if (n == 0)
                throw new EndOfStreamException("Unexpected end of IPP stream.");
            read += n;
        }
    }
}
