// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>A survey's own look (W26): picked from the gallery, changed, shown to respondents, dropped again.</summary>
public sealed class SurveyLookApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static JsonObject Konfetti()
    {
        var tokens = ThemesApiTests.Werkstatt();
        tokens["colours"]!["accent"] = "#c2391a";
        tokens["corners"] = "pill";
        tokens["controls"] = "tiles";
        tokens["buttons"] = "soft";
        tokens["decoration"] = new JsonObject { ["pattern"] = "confetti" };
        tokens["components"] = new JsonObject { ["choices"] = new JsonObject { ["columns"] = "two", ["selected"] = "fill" } };
        tokens["pages"] = new JsonObject { ["welcome"] = new JsonObject { ["layout"] = "band" } };

        return tokens;
    }

    [Fact]
    public async Task Look_IsTheSurveysOwn_ShownToRespondents_AndDroppedAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{surveyId}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var respondent = await factory.CreateReadyClientAsync();
        async Task<JsonElement> SeenAsync() => (await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{surveyId}"), Cancel)).GetProperty("survey").GetProperty("theme");
        async Task<JsonElement> LookAsync() => await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{surveyId}/look"), Cancel);

        var before = await LookAsync();
        var set = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/look"), new { tokens = Konfetti(), lookBase = "preset:konfetti" }, Cancel);
        var after = await LookAsync();
        var seen = await SeenAsync();
        var dropped = await owner.DeleteAsync(Url($"/api/app/surveys/{surveyId}/look"), Cancel);
        var back = await LookAsync();

        Assert.Equal((false, "ruler"), (before.GetProperty("own").GetBoolean(), before.GetProperty("tokens").GetProperty("progress").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal((true, "preset:konfetti"), (after.GetProperty("own").GetBoolean(), after.GetProperty("lookBase").GetString()));
        Assert.Equal("tiles", seen.GetProperty("controls").GetString());
        Assert.Equal("confetti", seen.GetProperty("decoration").GetProperty("pattern").GetString());
        Assert.Equal("two", seen.GetProperty("components").GetProperty("choices").GetProperty("columns").GetString());
        Assert.Equal("band", seen.GetProperty("pages").GetProperty("welcome").GetProperty("layout").GetString());
        Assert.Equal(HttpStatusCode.NoContent, dropped.StatusCode);
        Assert.Equal((false, JsonValueKind.Null), (back.GetProperty("own").GetBoolean(), back.GetProperty("lookBase").ValueKind));
    }

    [Fact]
    public async Task Look_IsCheckedLikeATheme_AndDarkModeNeedsDarkColours()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        var dark = Konfetti();
        dark["mode"] = "dark";

        var refused = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/look"), new { tokens = dark }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var issue = (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("issues")[0];
        Assert.Equal(("dark-colours-missing", "darkColours"), (issue.GetProperty("code").GetString(), issue.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task Duplicate_KeepsTheOwnLook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/look"), new { tokens = Konfetti(), lookBase = "preset:konfetti" }, Cancel)).EnsureSuccessStatusCode();

        var copy = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{surveyId}/duplicate"), new { title = "Copy" }, Cancel);
        var copyId = (await copy.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetString();
        var look = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{copyId}/look"), Cancel);

        Assert.Equal(("preset:konfetti", "pill"), (look.GetProperty("lookBase").GetString(), look.GetProperty("tokens").GetProperty("corners").GetString()));
    }
}
