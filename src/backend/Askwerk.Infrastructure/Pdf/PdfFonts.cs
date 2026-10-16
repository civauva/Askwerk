// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using PdfSharp.Fonts;

namespace Askwerk.Infrastructure.Pdf;

/// <summary>
/// Fonts for survey PDFs (AWK-17 step 7): the self-hosted IBM Plex faces (OFL, Pdf/Fonts/OFL.txt), embedded as WOFF
/// and unpacked to TrueType, which PDFsharp embeds into each PDF. Families a PDF cannot embed fall back to Plex Sans.
/// </summary>
public sealed class PdfFonts : IFontResolver
{
    private static readonly Dictionary<(string Family, bool Bold), string> Faces = new()
    {
        [("IBM Plex Sans", false)] = "ibm-plex-sans-latin-400-normal",
        [("IBM Plex Sans", true)] = "ibm-plex-sans-latin-600-normal",
        [("IBM Plex Sans Condensed", false)] = "ibm-plex-sans-condensed-latin-600-normal",
        [("IBM Plex Sans Condensed", true)] = "ibm-plex-sans-condensed-latin-700-normal",
        [("IBM Plex Serif", false)] = "ibm-plex-serif-latin-400-normal",
        [("IBM Plex Serif", true)] = "ibm-plex-serif-latin-700-normal",
        [("IBM Plex Mono", false)] = "ibm-plex-mono-latin-400-normal",
        [("IBM Plex Mono", true)] = "ibm-plex-mono-latin-400-normal",
    };

    private static readonly ConcurrentDictionary<string, byte[]> Unpacked = new();
    private static readonly Lock InstallLock = new();

    /// <summary>The family a PDF uses for a theme's font: the same when Askwerk has it, else IBM Plex Sans.</summary>
    /// <param name="family">The theme's family.</param>
    /// <returns>A family the PDF can embed.</returns>
    public static string Usable(string? family) => family is not null && Faces.ContainsKey((family, false)) ? family : "IBM Plex Sans";

    /// <summary>The TrueType bytes of a face.</summary>
    /// <param name="family">One of the IBM Plex families.</param>
    /// <param name="bold">Bold or regular.</param>
    /// <returns>The font file.</returns>
    public static byte[] Face(string family, bool bold) => Load(Faces[(Usable(family), bold)]);

    /// <summary>Makes PDFsharp use these fonts; safe to call more than once.</summary>
    public static void Install()
    {
        lock (InstallLock)
        {
            if (GlobalFontSettings.FontResolver is not PdfFonts)
            {
                GlobalFontSettings.FontResolver = new PdfFonts();
            }
        }
    }

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(Faces[(Usable(familyName), bold)]);

    /// <inheritdoc />
    public byte[]? GetFont(string faceName) => Load(faceName);

    private static byte[] Load(string face) => Unpacked.GetOrAdd(face, name =>
    {
        using var stream = typeof(PdfFonts).Assembly.GetManifestResourceStream($"Askwerk.Infrastructure.Pdf.Fonts.{name}.woff")
            ?? throw new InvalidOperationException($"Font {name} is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return Woff.ToTrueType(buffer.ToArray());
    });
}

/// <summary>Unpacks WOFF 1.0 (zlib-compressed tables) into the plain TrueType/OpenType file it wraps.</summary>
public static class Woff
{
    /// <summary>The font file inside a WOFF file.</summary>
    /// <param name="woff">The WOFF bytes.</param>
    /// <returns>TrueType or OpenType bytes.</returns>
    public static byte[] ToTrueType(byte[] woff)
    {
        ArgumentNullException.ThrowIfNull(woff);
        ReadOnlySpan<byte> data = woff;

        if (!data[..4].SequenceEqual("wOFF"u8))
        {
            throw new ArgumentException("Not a WOFF file.", nameof(woff));
        }

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var count = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        var tables = new List<(uint Tag, uint Checksum, byte[] Bytes)>();

        for (var index = 0; index < count; index++)
        {
            var entry = data.Slice(44 + (index * 20), 20);
            var tag = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var compressed = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var original = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            var checksum = BinaryPrimitives.ReadUInt32BigEndian(entry[16..]);
            var bytes = woff.AsSpan(offset, compressed).ToArray();

            if (compressed < original)
            {
                using var zlib = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var unpacked = new MemoryStream(original);
                zlib.CopyTo(unpacked);
                bytes = unpacked.ToArray();
            }

            tables.Add((tag, checksum, bytes));
        }

        var power = 1;
        var log = 0;

        while (power * 2 <= count)
        {
            power *= 2;
            log++;
        }

        using var font = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(header, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], count);
        BinaryPrimitives.WriteUInt16BigEndian(header[6..], (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(header[8..], (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)((count * 16) - (power * 16)));
        font.Write(header);

        var position = 12 + (count * 16);
        Span<byte> record = stackalloc byte[16];

        foreach (var (tag, checksum, bytes) in tables)
        {
            BinaryPrimitives.WriteUInt32BigEndian(record, tag);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], checksum);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)bytes.Length);
            font.Write(record);
            position += (bytes.Length + 3) & ~3;
        }

        foreach (var (_, _, bytes) in tables)
        {
            font.Write(bytes);
            font.Write(new byte[((bytes.Length + 3) & ~3) - bytes.Length]);
        }

        return font.ToArray();
    }
}
