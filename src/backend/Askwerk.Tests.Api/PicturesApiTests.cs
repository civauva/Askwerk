// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Infrastructure.Pictures;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Askwerk's background pictures (AWK-74): public-domain photos by topic, copied into the organisation's files.</summary>
public sealed class PicturesApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public void Pictures_AreBuiltIn_ByTopic_WithTheirSourceAndAPublicDomainLicence()
    {
        Assert.NotEmpty(AskwerkPictures.All);
        Assert.All(AskwerkPictures.All, picture =>
        {
            Assert.Contains(picture.Topic, AskwerkPictures.Topics);
            Assert.True(picture.License is "cc0" or "pdm" || picture is { License: "askwerk", Topic: "illustrations" }, picture.License);
            Assert.StartsWith("https://", picture.Page, StringComparison.Ordinal);
            Assert.InRange(picture.Width, 1600, 2400);
            Assert.NotEmpty(AskwerkPictures.Image(picture));
            Assert.NotEmpty(AskwerkPictures.Thumbnail(picture));
        });
        Assert.All(AskwerkPictures.Topics, topic => Assert.Contains(AskwerkPictures.All, picture => picture.Topic == topic));
    }

    [Fact]
    public void Illustrations_AreTheSamplesDrawings_At16To10()
    {
        var drawn = AskwerkPictures.All.Where(picture => picture.Topic == "illustrations").ToList();

        Assert.InRange(drawn.Count, 6, 12);
        Assert.All(drawn, picture =>
        {
            using var image = SkiaSharp.SKBitmap.Decode(AskwerkPictures.Image(picture));
            Assert.Equal((2400, 1500), (image.Width, image.Height));
        });
    }

    [Fact]
    public async Task UsePicture_CopiesItIntoTheOrganisationsFiles_AndUnknownOnesAreNotFound()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var first = AskwerkPictures.All[0];

        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/pictures"), Cancel);
        var thumbnail = await owner.GetAsync(Url($"/api/app/pictures/{first.Id}/thumbnail"), Cancel);
        var used = await owner.PostAsync(Url($"/api/app/pictures/{first.Id}/use"), null, Cancel);
        var assetId = (await used.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("assetId").GetString();
        var asset = await owner.GetAsync(Url($"/api/app/assets/{assetId}"), Cancel);
        var unknown = await owner.PostAsync(Url("/api/app/pictures/no-such-picture/use"), null, Cancel);

        Assert.Equal(AskwerkPictures.All.Count, list.GetProperty("items").GetArrayLength());
        Assert.Equal(("image/webp", HttpStatusCode.Created), (thumbnail.Content.Headers.ContentType?.MediaType, used.StatusCode));
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(AskwerkPictures.Image(first), await asset.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
