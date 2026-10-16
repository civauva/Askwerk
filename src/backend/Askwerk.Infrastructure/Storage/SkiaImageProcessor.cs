// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Storage;
using SkiaSharp;

namespace Askwerk.Infrastructure.Storage;

/// <summary>Images with SkiaSharp (MIT; decisions.md, AWK-17): decoding checks a file really is an image.</summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>Largest picture accepted, in pixels (e.g. 8000 × 5000).</summary>
    public const long MaxPixels = 40_000_000;

    /// <inheritdoc />
    public ImageSize? Measure(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);

        // A small file can unpack into a huge picture; above 40 megapixels it is refused before decoding.
        if (codec is null || codec.Info.Width == 0 || codec.Info.Height == 0 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        // Every pixel must decode; a cut-off or tampered file is not an image.
        using var bitmap = new SKBitmap(codec.Info);

        return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success ? new ImageSize(codec.Info.Width, codec.Info.Height) : null;
    }

    /// <inheritdoc />
    public byte[] Resize(byte[] image, int width, string contentType)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var source = SKBitmap.Decode(image) ?? throw new ArgumentException("Not an image.", nameof(image));
        var height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
        using var resized = source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("The image could not be resized.");
        using var encoded = resized.Encode(contentType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        }, 85);

        return encoded.ToArray();
    }
}
