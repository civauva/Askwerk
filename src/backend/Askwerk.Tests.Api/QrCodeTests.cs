// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Infrastructure.Pdf;
using SkiaSharp;

namespace Askwerk.Tests.Api;

/// <summary>The server's QR encoder draws exactly what the browser's does (shared corpus in packages/ui, AWK-17).</summary>
public sealed class QrCodeTests
{
    public static TheoryData<string> Cases()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (!File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent!;
        }

        return new(Directory.GetFiles(Path.Combine(root.FullName, "src", "frontend", "packages", "ui", "tests", "corpus", "qr"), "*.json").Order());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Encode_CorpusCase_DrawsTheSameModules(string file)
    {
        var corpusCase = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file));
        var expected = corpusCase.GetProperty("modules").EnumerateArray().Select(row => row.GetString()).ToList();

        var modules = QrCode.Encode(corpusCase.GetProperty("text").GetString()!);

        Assert.Equal(expected, modules.Select(row => string.Concat(row.Select(dark => dark ? '1' : '0'))));
    }

    [Fact]
    public void Encode_TooLongText_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QrCode.Encode(new string('x', 300)));
    }

    [Fact]
    public void Png_ForThePdf_HasAQuietZoneAndCrispModules()
    {
        const string link = "https://askwerk.example/s/x";
        using var image = SKBitmap.Decode(QrCode.Png(link, moduleSize: 4));
        var side = (QrCode.Encode(link).Length + 8) * 4;

        Assert.Equal((side, side), (image.Width, image.Height));
        Assert.Equal(SKColors.White, image.GetPixel(0, 0));
        Assert.Equal(SKColors.Black, image.GetPixel(4 * 4, 4 * 4));
    }
}
