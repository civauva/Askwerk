// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Themes and uploaded files of an organisation (AWK-17 step 1, W10 rev. 2), and the theme the runner gets.</summary>
public sealed class ThemesApiTests(PostgresServer server)
{
    /// <summary>The smallest valid PNG: one transparent pixel.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    internal static JsonObject Werkstatt() => new()
    {
        ["schemaVersion"] = 1,
        ["colours"] = new JsonObject
        {
            ["accent"] = "#d2440f", ["onAccent"] = "#ffffff", ["background"] = "#f6f5f2", ["surface"] = "#ffffff",
            ["text"] = "#1a1917", ["muted"] = "#6e6962", ["line"] = "#c8c2b8",
        },
        ["fonts"] = new JsonObject { ["heading"] = new JsonObject { ["family"] = "IBM Plex Sans Condensed" }, ["body"] = new JsonObject { ["family"] = "IBM Plex Sans" } },
        ["scale"] = "m",
        ["spacing"] = "normal",
        ["corners"] = "sharp",
        ["buttons"] = "filled",
        ["controls"] = "boxes",
        ["progress"] = "ruler",
        ["layout"] = "card",
    };

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, JsonObject tokens) =>
        client.PostAsJsonAsync(Url("/api/app/themes"), new { name, tokens }, Cancel);

    private static async Task<string> CreatedIdAsync(HttpClient client, string name, JsonObject tokens)
    {
        var created = await CreateAsync(client, name, tokens);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await JsonAsync(created)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement[]> ThemesAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Url("/api/app/themes"), Cancel)).GetProperty("items").EnumerateArray()];

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName, string contentType, string kind = "image")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        content.Add(new StringContent(kind), "kind");

        return client.PostAsync(Url("/api/app/assets"), content, Cancel);
    }

    [Fact]
    public async Task Create_ChecksTheTheme_AndTheFirstBecomesTheDefault()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var weak = Werkstatt();
        weak["colours"]!["text"] = "#9a948c";

        var refused = await CreateAsync(owner, "Grey", weak);
        var first = await CreatedIdAsync(owner, "Acme Standard", Werkstatt());
        var second = await CreatedIdAsync(owner, "Acme Event", Werkstatt());
        var themes = await ThemesAsync(owner);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var issue = (await JsonAsync(refused)).GetProperty("issues")[0];
        Assert.Equal(("contrast-too-low", "colours/text", 3.0), (issue.GetProperty("code").GetString(), issue.GetProperty("path").GetString(), issue.GetProperty("ratio").GetDouble()));
        Assert.Equal([(first, "Acme Standard", true), (second, "Acme Event", false)], themes.Select(theme => (theme.GetProperty("id").GetString(), theme.GetProperty("name").GetString(), theme.GetProperty("isDefault").GetBoolean())));
        Assert.Equal("#d2440f", themes[0].GetProperty("tokens").GetProperty("colours").GetProperty("accent").GetString());
    }

    [Fact]
    public async Task Update_MakeDefault_AndDelete_KeepOneDefault()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var first = await CreatedIdAsync(owner, "Acme Standard", Werkstatt());
        var second = await CreatedIdAsync(owner, "Acme Event", Werkstatt());
        var changed = Werkstatt();
        changed["corners"] = "rounded";

        var updated = await owner.PutAsJsonAsync(Url($"/api/app/themes/{second}"), new { name = "Event", tokens = changed }, Cancel);
        var madeDefault = await owner.PostAsync(Url($"/api/app/themes/{second}/default"), null, Cancel);
        var deleteDefault = await owner.DeleteAsync(Url($"/api/app/themes/{second}"), Cancel);
        var deleteOther = await owner.DeleteAsync(Url($"/api/app/themes/{first}"), Cancel);
        var themes = await ThemesAsync(owner);

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.Conflict, HttpStatusCode.NoContent], [updated.StatusCode, madeDefault.StatusCode, deleteDefault.StatusCode, deleteOther.StatusCode]);
        Assert.Equal("theme-is-default", (await JsonAsync(deleteDefault)).GetProperty("code").GetString());
        var only = Assert.Single(themes);
        Assert.Equal(("Event", true, "rounded"), (only.GetProperty("name").GetString(), only.GetProperty("isDefault").GetBoolean(), only.GetProperty("tokens").GetProperty("corners").GetString()));
    }

    [Fact]
    public async Task Editors_SeeThemes_ButOnlyOwnersAndAdminsChangeThem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await CreatedIdAsync(owner, "Acme Standard", Werkstatt());
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");

        var list = await owner.GetAsync(Url("/api/app/themes"), Cancel);
        var created = await CreateAsync(owner, "Mine", Werkstatt());

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden), (list.StatusCode, created.StatusCode));
    }

    [Fact]
    public async Task Upload_StoresImagesAndFonts_CheckedByTheirContent_AndGivesThemBack()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        byte[] woff2 = [.. "wOF2"u8.ToArray(), .. new byte[60]];

        var image = await UploadAsync(owner, Png, "logo.png", "image/png");
        var font = await UploadAsync(owner, woff2, "Brand.woff2", "font/woff2", "font");
        var disguised = await UploadAsync(owner, "MZ not an image"u8.ToArray(), "logo.png", "image/png");
        var fontAsImage = await UploadAsync(owner, woff2, "Brand.png", "image/png");
        var tooLarge = await UploadAsync(owner, [.. Png, .. new byte[(5 * 1024 * 1024) + 1]], "huge.png", "image/png");

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (image.StatusCode, font.StatusCode));
        var stored = await JsonAsync(image);
        Assert.Equal(("image", "image/png", Png.Length), (stored.GetProperty("kind").GetString(), stored.GetProperty("contentType").GetString(), stored.GetProperty("size").GetInt32()));
        Assert.Equal("font/woff2", (await JsonAsync(font)).GetProperty("contentType").GetString());
        Assert.Equal("file-type-not-allowed", (await JsonAsync(disguised)).GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Equal("file-type-not-allowed", (await JsonAsync(fontAsImage)).GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Equal("file-too-large", (await JsonAsync(tooLarge)).GetProperty("errors").GetProperty("file")[0].GetString());

        var back = await owner.GetAsync(Url($"/api/app/assets/{stored.GetProperty("id").GetString()}"), Cancel);
        Assert.Equal("image/png", back.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await back.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task Runner_GetsTheSurveysTheme_OrTheDefault_OrWerkstatt_AndOnlyItsImages()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{surveyId}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var respondent = await factory.CreateReadyClientAsync();
        async Task<JsonElement> ThemeSeenAsync() => (await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{surveyId}"), Cancel)).GetProperty("survey").GetProperty("theme");

        var withoutThemes = await ThemeSeenAsync();
        var logo = (await JsonAsync(await UploadAsync(owner, Png, "logo.png", "image/png"))).GetProperty("id").GetString()!;
        var other = (await JsonAsync(await UploadAsync(owner, Png, "other.png", "image/png"))).GetProperty("id").GetString()!;
        var standard = Werkstatt();
        standard["logo"] = new JsonObject { ["asset"] = logo };
        await CreatedIdAsync(owner, "Acme Standard", standard);
        var withDefault = await ThemeSeenAsync();
        var klar = Werkstatt();
        klar["layout"] = "minimal";
        var event1 = await CreatedIdAsync(owner, "Acme Event", klar);
        var chosen = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/theme"), new { themeId = event1 }, Cancel);
        var withOwn = await ThemeSeenAsync();
        await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/theme"), new { themeId = (string?)null }, Cancel);
        var logoFile = await respondent.GetAsync(Url($"/api/app/run/{surveyId}/assets/{logo}"), Cancel);
        var otherFile = await respondent.GetAsync(Url($"/api/app/run/{surveyId}/assets/{other}"), Cancel);

        Assert.Equal(("card", JsonValueKind.Undefined), (withoutThemes.GetProperty("layout").GetString(), withoutThemes.TryGetProperty("logo", out _) ? JsonValueKind.Object : JsonValueKind.Undefined));
        Assert.Equal(logo, withDefault.GetProperty("logo").GetProperty("asset").GetString());
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        Assert.Equal("minimal", withOwn.GetProperty("layout").GetString());
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (logoFile.StatusCode, otherFile.StatusCode));
        Assert.Equal(Png, await logoFile.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task Draft_SaysWhichThemeTheSurveyUses()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        var theme = await CreatedIdAsync(owner, "Acme Event", Werkstatt());
        async Task<JsonElement> ThemeIdAsync() => (await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{surveyId}/draft"), Cancel)).GetProperty("themeId");

        var before = await ThemeIdAsync();
        await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/theme"), new { themeId = theme }, Cancel);
        var after = await ThemeIdAsync();

        Assert.Equal((JsonValueKind.Null, theme), (before.ValueKind, after.GetString()));
    }

    [Fact]
    public async Task Runner_AlsoGetsTheSurveysOwnHeaderImage()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var header = (await JsonAsync(await UploadAsync(owner, Png, "shop.png", "image/png"))).GetProperty("id").GetString()!;
        var definition = RunnerApiTests.Definition();
        definition["headerImage"] = new JsonObject { ["asset"] = header, ["alt"] = new JsonObject { ["de"] = "Unsere Filiale", ["en"] = "Our shop" } };
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{surveyId}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var respondent = await factory.CreateReadyClientAsync();

        var file = await respondent.GetAsync(Url($"/api/app/run/{surveyId}/assets/{header}"), Cancel);

        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
    }

    [Fact]
    public async Task Runner_GetsTheImagesOfTheQuestions_AndTheirPictureChoices()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        async Task<string> NewImageAsync(string name) => (await JsonAsync(await UploadAsync(owner, Png, name, "image/png"))).GetProperty("id").GetString()!;
        var block = await NewImageAsync("block.png");
        var picture = await NewImageAsync("vienna.png");
        var unused = await NewImageAsync("unused.png");
        var definition = RunnerApiTests.Definition();
        var questions = definition["pages"]![0]!["questions"]!.AsArray();
        questions.Insert(0, new JsonObject
        {
            ["id"] = "img", ["variable"] = "IMG_SHOP", ["type"] = "image", ["text"] = new JsonObject(), ["required"] = false,
            ["image"] = new JsonObject { ["asset"] = block, ["alt"] = new JsonObject { ["de"] = "Filiale", ["en"] = "Shop" } },
        });
        questions[2]!["options"]![0]!["image"] = new JsonObject { ["asset"] = picture };
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{surveyId}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var respondent = await factory.CreateReadyClientAsync();
        async Task<HttpStatusCode> StatusAsync(string asset) => (await respondent.GetAsync(Url($"/api/app/run/{surveyId}/assets/{asset}"), Cancel)).StatusCode;

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound], [await StatusAsync(block), await StatusAsync(picture), await StatusAsync(unused)]);
    }
}
