// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Assets;

/// <summary>What an uploaded file is for.</summary>
public enum AssetKind
{
    /// <summary>An image: logo, header, background, image block.</summary>
    Image,

    /// <summary>A font file for a theme.</summary>
    Font,
}

/// <summary>
/// A file the organisation uploaded (AWK-17): its bytes live in file storage under <see cref="StorageKey"/>; this row
/// holds what was checked about it.
/// </summary>
public sealed class Asset
{
    private Asset()
    {
        ContentType = "";
        OriginalName = "";
        Sha256 = "";
        StorageKey = "";
    }

    private Asset(Guid id, Guid tenantId, AssetKind kind, string contentType, long size, string sha256, string originalName, string storageKey, Guid createdBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Kind = kind;
        ContentType = contentType;
        Size = size;
        Sha256 = sha256;
        OriginalName = originalName;
        StorageKey = storageKey;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets what the file is for.</summary>
    public AssetKind Kind { get; private set; }

    /// <summary>Gets the media type found from the content, not the one the browser claimed.</summary>
    public string ContentType { get; private set; }

    /// <summary>Gets the size in bytes.</summary>
    public long Size { get; private set; }

    /// <summary>Gets the SHA-256 of the content, hex.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Gets the file name as uploaded, for display only.</summary>
    public string OriginalName { get; private set; }

    /// <summary>Gets where file storage keeps the bytes.</summary>
    public string StorageKey { get; private set; }

    /// <summary>Gets the width in pixels; images only.</summary>
    public int? Width { get; private set; }

    /// <summary>Gets the height in pixels; images only.</summary>
    public int? Height { get; private set; }

    /// <summary>Gets who uploaded it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Gets when it was uploaded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A checked upload.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="kind">Image or font.</param>
    /// <param name="contentType">Media type found from the content.</param>
    /// <param name="size">Size in bytes.</param>
    /// <param name="sha256">Hash of the content.</param>
    /// <param name="originalName">Name as uploaded.</param>
    /// <param name="storageKey">Key in file storage.</param>
    /// <param name="createdBy">The user.</param>
    /// <param name="now">The time.</param>
    /// <param name="width">Images: width in pixels.</param>
    /// <param name="height">Images: height in pixels.</param>
    /// <returns>The asset.</returns>
    public static Asset Create(Guid id, Guid tenantId, AssetKind kind, string contentType, long size, string sha256, string? originalName, string storageKey, Guid createdBy, DateTimeOffset now, int? width = null, int? height = null)
    {
        var name = Path.GetFileName(originalName ?? "").Trim();

        return new Asset(id, tenantId, kind, contentType, size, sha256, name.Length == 0 ? "file" : name[..Math.Min(name.Length, 200)], storageKey, createdBy, now)
        {
            Width = width,
            Height = height,
        };
    }

    /// <summary>The width of the copy to serve for a wanted width: the next of 480, 960, 1600 or 2400, never more than the image.</summary>
    /// <param name="wanted">Pixels the page needs.</param>
    /// <returns>A width smaller than the image, or null for the original.</returns>
    public int? CopyWidthFor(int wanted)
    {
        int[] widths = [480, 960, 1600, 2400];
        var copy = widths.FirstOrDefault(width => width >= wanted, widths[^1]);

        return Kind == AssetKind.Image && Width is { } own && copy < own ? copy : null;
    }
}

/// <summary>Which files may be uploaded, told by their first bytes; the name and the claimed type do not count.</summary>
public static class AssetTypes
{
    /// <summary>Largest image.</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Largest font file.</summary>
    public const long MaxFontBytes = 2 * 1024 * 1024;

    /// <summary>The largest file of a kind.</summary>
    /// <param name="kind">Image or font.</param>
    /// <returns>Bytes.</returns>
    public static long MaxBytes(AssetKind kind) => kind == AssetKind.Font ? MaxFontBytes : MaxImageBytes;

    /// <summary>The media type of the content when it is allowed for the kind; null otherwise.</summary>
    /// <param name="kind">Image or font.</param>
    /// <param name="head">The first bytes of the file (12 are enough).</param>
    /// <returns>For example "image/png" or "font/woff2"; null when not allowed.</returns>
    public static string? Detect(AssetKind kind, ReadOnlySpan<byte> head)
    {
        if (kind == AssetKind.Image)
        {
            if (head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            {
                return "image/png";
            }

            if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            {
                return "image/jpeg";
            }

            return head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8) ? "image/webp" : null;
        }

        if (head.StartsWith("wOF2"u8))
        {
            return "font/woff2";
        }

        if (head.StartsWith("wOFF"u8))
        {
            return "font/woff";
        }

        if (head.StartsWith("OTTO"u8))
        {
            return "font/otf";
        }

        return head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x01, 0x00, 0x00]) || head.StartsWith("true"u8) ? "font/ttf" : null;
    }
}
