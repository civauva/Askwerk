// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Application.Storage;

/// <summary>Width and height in pixels.</summary>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
public sealed record ImageSize(int Width, int Height);

/// <summary>
/// Reads and resizes uploaded images (AWK-17): one place for the image library, so it can be replaced in one place
/// (agents/coding-standards.md, Dependencies).
/// </summary>
public interface IImageProcessor
{
    /// <summary>The size of an image, or null when the bytes are not an image that can be decoded.</summary>
    /// <param name="image">The file.</param>
    /// <returns>The size, or null.</returns>
    ImageSize? Measure(byte[] image);

    /// <summary>A copy at the given width, keeping the aspect ratio and the format.</summary>
    /// <param name="image">The file.</param>
    /// <param name="width">Pixels across; smaller than the image's own width.</param>
    /// <param name="contentType">image/png, image/jpeg or image/webp.</param>
    /// <returns>The encoded copy.</returns>
    byte[] Resize(byte[] image, int width, string contentType);
}
