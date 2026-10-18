// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Askwerk.Infrastructure.Samples;
using SkiaSharp;

namespace Askwerk.Infrastructure.Pictures;

/// <summary>One of Askwerk's background pictures (AWK-74): a public-domain photo, its topic and where it comes from.</summary>
/// <param name="Id">Its key, e.g. "holidays-abc123".</param>
/// <param name="Topic">charity, technology, finance, holidays, animals or illustrations.</param>
/// <param name="Title">The photo's title at its source.</param>
/// <param name="Creator">Who took it, when known.</param>
/// <param name="License">cc0 or pdm; askwerk for Askwerk's own drawings.</param>
/// <param name="Source">Where it was found, e.g. wikimedia; askwerk for Askwerk's own drawings.</param>
/// <param name="Page">The photo's page at its source.</param>
/// <param name="Width">Width in pixels (at most 2400).</param>
/// <param name="Height">Height in pixels (16:10).</param>
public sealed record AskwerkPicture(
    string Id,
    string Topic,
    string Title,
    string? Creator,
    string License,
    string Source,
    string Page,
    int Width,
    int Height);

/// <summary>
/// Askwerk's background pictures (AWK-74), built into the server: picked by the owner, cropped to 16:10 and compressed
/// to WebP by bin/import-pictures.py. Using one copies it into the organisation's files, so a survey keeps it whatever
/// happens to this list. The illustrations are Askwerk's own drawings, the ones the sample surveys use, drawn when
/// first asked for.
/// </summary>
public static class AskwerkPictures
{
    private const string Prefix = "Askwerk.Infrastructure.Pictures.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Drawn = "askwerk";
    private static readonly Lazy<IReadOnlyList<AskwerkPicture>> List = new(Load);
    private static readonly ConcurrentDictionary<string, byte[]> Drawings = new();

    // The sample surveys' drawings that work behind a survey, with their titles.
    private static readonly (string Scene, string Title)[] Illustrations =
    [
        ("peaks", "Mountains at dawn"),
        ("beach", "Beach"),
        ("forest", "Forest"),
        ("city", "City"),
        ("garden", "Café garden"),
        ("cafe", "Café"),
        ("museum", "Museum hall"),
        ("team", "Team"),
    ];

    /// <summary>The topics, in the order the picture panel shows them.</summary>
    public static IReadOnlyList<string> Topics { get; } = ["charity", "technology", "finance", "holidays", "animals", "illustrations"];

    /// <summary>Every picture, by topic in the order picked.</summary>
    public static IReadOnlyList<AskwerkPicture> All => List.Value;

    /// <summary>A picture by its key.</summary>
    /// <param name="id">The key.</param>
    /// <returns>The picture, or null for an unknown key.</returns>
    public static AskwerkPicture? Find(string id) => All.FirstOrDefault(picture => picture.Id == id);

    /// <summary>The picture itself (WebP).</summary>
    /// <param name="picture">The picture.</param>
    /// <returns>Its bytes.</returns>
    public static byte[] Image(AskwerkPicture picture) =>
        picture.Source == Drawn ? Draw(picture, picture.Width, picture.Height, 82) : Read($"{Prefix}{picture.Id}.webp");

    /// <summary>Its 480 × 300 thumbnail (WebP), for the picture panel.</summary>
    /// <param name="picture">The picture.</param>
    /// <returns>Its bytes.</returns>
    public static byte[] Thumbnail(AskwerkPicture picture) =>
        picture.Source == Drawn ? Draw(picture, 480, 300, 72) : Read($"{Prefix}{picture.Id}.thumb.webp");

    private static byte[] Draw(AskwerkPicture picture, int width, int height, int quality) =>
        Drawings.GetOrAdd($"{picture.Id}@{width}", _ =>
        {
            using var image = SampleSurveys.Covering(picture.Id["illustration-".Length..], width, height);
            using var data = image.Encode(SKEncodedImageFormat.Webp, quality);

            return data.ToArray();
        });

    private static byte[] Read(string name)
    {
        using var stream = typeof(AskwerkPictures).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing picture {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    private static IReadOnlyList<AskwerkPicture> Load()
    {
        using var stream = typeof(AskwerkPictures).Assembly.GetManifestResourceStream($"{Prefix}pictures.json");

        IReadOnlyList<AskwerkPicture> photos = stream is null ? [] : JsonSerializer.Deserialize<PictureFile>(stream, Json)?.Pictures ?? [];
        var drawn = Illustrations.Select(illustration => new AskwerkPicture(
            $"illustration-{illustration.Scene}", "illustrations", illustration.Title, "Askwerk", Drawn, Drawn, "https://github.com/civauva/Askwerk", 2400, 1500));

        return [.. photos, .. drawn];
    }

    private sealed record PictureFile([property: JsonPropertyName("pictures")] IReadOnlyList<AskwerkPicture> Pictures);
}
