// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Uploaded images are checked by decoding them, and served in smaller copies for phones, email and PDF (AWK-17 step 3).</summary>
public sealed class AssetImagesApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>A real PNG of one colour, built without an image library.</summary>
    internal static byte[] SolidPng(int width, int height)
    {
        using var raw = new MemoryStream();

        for (var row = 0; row < height; row++)
        {
            raw.WriteByte(0); // no filter

            for (var column = 0; column < width; column++)
            {
                raw.Write([0x0b, 0x6e, 0x4f]);
            }
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    /// <summary>Width and height from a PNG's header.</summary>
    internal static (int Width, int Height) PngSize(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    /// <summary>The CRC-32 a PNG chunk ends with (ISO 3309), so the test needs no hashing package.</summary>
    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static async Task<JsonElement> UploadAsync(HttpClient client, byte[] bytes, string name)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", name);
        content.Add(new StringContent("image"), "kind");
        var answer = await client.PostAsync(Url("/api/app/assets"), content, Cancel);

        return await answer.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    [Fact]
    public async Task Upload_DecodesTheImage_AndRecordsItsSize()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var broken = SolidPng(10, 10)[..40];

        var stored = await UploadAsync(owner, SolidPng(1200, 600), "header.png");
        var refused = await UploadAsync(owner, broken, "broken.png");

        Assert.Equal((1200, 600), (stored.GetProperty("width").GetInt32(), stored.GetProperty("height").GetInt32()));
        Assert.Equal("file-type-not-allowed", refused.GetProperty("errors").GetProperty("file")[0].GetString());
    }

    [Fact]
    public async Task Get_GivesASmallerCopy_ForAWidth_AndNeverEnlarges()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var original = SolidPng(1200, 600);
        var id = (await UploadAsync(owner, original, "header.png")).GetProperty("id").GetString();

        var small = await owner.GetAsync(Url($"/api/app/assets/{id}?w=480"), Cancel);
        var again = await owner.GetAsync(Url($"/api/app/assets/{id}?w=500"), Cancel);
        var large = await owner.GetAsync(Url($"/api/app/assets/{id}?w=2400"), Cancel);

        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
        Assert.Equal("image/png", small.Content.Headers.ContentType?.MediaType);
        Assert.Equal((480, 240), PngSize(await small.Content.ReadAsByteArrayAsync(Cancel)));
        Assert.Equal((960, 480), PngSize(await again.Content.ReadAsByteArrayAsync(Cancel)));
        Assert.Equal(original, await large.Content.ReadAsByteArrayAsync(Cancel));
    }
}
