// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Preview of the draft (AWK-22, W9): editors try the survey in the real runner; nothing is stored.</summary>
public sealed class PreviewApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task<HttpResponseMessage> CheckPageAsync(HttpClient client, string id, string page, object answers) =>
        client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/preview/pages/{page}"), new { answers }, Cancel);

    /// <summary>A survey with only a saved draft, never published.</summary>
    private static async Task<string> DraftAsync(HttpClient owner)
    {
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();

        return id;
    }

    [Fact]
    public async Task Open_GivesEditorsTheDraft_InTheRunnersShape()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);

        var opened = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/preview"), Cancel);

        var survey = opened.GetProperty("survey");
        Assert.Equal((id, "Acme GmbH", "open"), (survey.GetProperty("id").GetString(), survey.GetProperty("organisation").GetString(), survey.GetProperty("state").GetString()));
        Assert.Equal(2, survey.GetProperty("definition").GetProperty("pages").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, opened.GetProperty("response").ValueKind);
    }

    [Fact]
    public async Task Open_ShowsTheLastSavedDraft_NotThePublishedVersion()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        var changed = RunnerApiTests.Definition();
        changed["layout"] = "classic";
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = changed }, Cancel)).EnsureSuccessStatusCode();

        var opened = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/preview"), Cancel);

        Assert.Equal("classic", opened.GetProperty("survey").GetProperty("definition").GetProperty("layout").GetString());
    }

    [Fact]
    public async Task CheckPage_ChecksLikeTheRealSurvey_AndStoresNothing()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);

        var missing = await CheckPageAsync(owner, id, "p1", new { });
        var email = await CheckPageAsync(owner, id, "p2", new { q3 = new { option = "1" }, q4 = new { text = "anna@acme" } });
        var fine = await CheckPageAsync(owner, id, "p1", new { q1 = new { number = 4 } });
        var unknownPage = await CheckPageAsync(owner, id, "nope", new { });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("required", (await JsonAsync(missing)).GetProperty("errors").GetProperty("q1")[0].GetString());
        Assert.Equal("format-email", (await JsonAsync(email)).GetProperty("errors").GetProperty("q4")[0].GetString());
        Assert.Equal(HttpStatusCode.NoContent, fine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownPage.StatusCode);
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM response"));
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM answer"));
    }

    [Fact]
    public async Task Preview_IsForEveryMemberOfTheOrganisation_AndStillStoresNothing()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);
        using var stranger = await factory.CreateReadyClientAsync();

        var anonymous = await stranger.GetAsync(Url($"/api/app/surveys/{id}/preview"), Cancel);
        var unknown = await owner.GetAsync(Url($"/api/app/surveys/{Guid.NewGuid()}/preview"), Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");
        var viewer = await owner.GetAsync(Url($"/api/app/surveys/{id}/preview"), Cancel);
        var viewerCheck = await CheckPageAsync(owner, id, "p1", new { q1 = new { number = 4 } });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NoContent], [viewer.StatusCode, viewerCheck.StatusCode]);
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM response"));
    }
}
