// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using System.Text;

namespace Askwerk.Infrastructure.Dns;

/// <summary>A record in a DNS answer.</summary>
/// <param name="Type">Record type, e.g. <see cref="DnsMessage.Cname"/>.</param>
/// <param name="Data">CNAME: the target name; TXT: its strings joined.</param>
public sealed record DnsRecord(ushort Type, string Data);

/// <summary>
/// DNS messages as RFC 1035 writes them, for the two record types domain verification needs (AWK-7): our own small
/// client instead of a library, the way the QR encoder and the Excel writer are our own.
/// </summary>
public static class DnsMessage
{
    /// <summary>CNAME record type.</summary>
    public const ushort Cname = 5;

    /// <summary>TXT record type.</summary>
    public const ushort Txt = 16;

    /// <summary>A recursive query with one question.</summary>
    /// <param name="id">The query's id, matched against the answer.</param>
    /// <param name="name">The name asked about.</param>
    /// <param name="type">The record type.</param>
    /// <returns>The message bytes.</returns>
    public static byte[] Query(ushort id, string name, ushort type)
    {
        ArgumentNullException.ThrowIfNull(name);
        using var message = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        header[2] = 0x01; // recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);
        message.Write(header);

        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);

            if (bytes.Length is 0 or > 63)
            {
                throw new ArgumentException($"Not a DNS name: {name}.", nameof(name));
            }

            message.WriteByte((byte)bytes.Length);
            message.Write(bytes);
        }

        message.WriteByte(0);
        Span<byte> question = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(question, type);
        BinaryPrimitives.WriteUInt16BigEndian(question[2..], 1); // class IN
        message.Write(question);

        return message.ToArray();
    }

    /// <summary>The CNAME and TXT records of an answer.</summary>
    /// <param name="message">The answer's bytes.</param>
    /// <param name="id">The query's id.</param>
    /// <returns>The records, in order.</returns>
    /// <exception cref="FormatException">Not an answer to this query, or malformed.</exception>
    public static IReadOnlyList<DnsRecord> Parse(ReadOnlySpan<byte> message, ushort id)
    {
        try
        {
            if (message.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(message) != id || (message[2] & 0x80) == 0)
            {
                throw new FormatException("Not the answer to this query.");
            }

            var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
            var answers = BinaryPrimitives.ReadUInt16BigEndian(message[6..]);
            var offset = 12;

            for (var index = 0; index < questions; index++)
            {
                ReadName(message, ref offset);
                offset += 4;
            }

            var records = new List<DnsRecord>();

            for (var index = 0; index < answers; index++)
            {
                ReadName(message, ref offset);
                var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
                var length = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
                var start = offset + 10;
                var data = message.Slice(start, length);

                if (type == Cname)
                {
                    var at = start;
                    records.Add(new DnsRecord(type, ReadName(message, ref at)));
                }
                else if (type == Txt)
                {
                    var text = new StringBuilder();

                    for (var at = 0; at < data.Length; at += data[at] + 1)
                    {
                        text.Append(Encoding.UTF8.GetString(data.Slice(at + 1, data[at])));
                    }

                    records.Add(new DnsRecord(type, text.ToString()));
                }

                offset = start + length;
            }

            return records;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException("A malformed DNS answer.", exception);
        }
    }

    /// <summary>A name, following compression pointers; at most 16 jumps, so a loop cannot hang the reader.</summary>
    private static string ReadName(ReadOnlySpan<byte> message, ref int offset)
    {
        var labels = new List<string>();
        var at = offset;
        var jumped = false;

        for (var jumps = 0; ; )
        {
            var length = message[at];

            if (length == 0)
            {
                at++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (++jumps > 16)
                {
                    throw new FormatException("A loop in a DNS name.");
                }

                if (!jumped)
                {
                    offset = at + 2;
                    jumped = true;
                }

                at = ((length & 0x3F) << 8) | message[at + 1];
                continue;
            }

            labels.Add(Encoding.ASCII.GetString(message.Slice(at + 1, length)));
            at += length + 1;
        }

        if (!jumped)
        {
            offset = at;
        }

        return string.Join('.', labels);
    }
}
