// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// The survey's menu and bar (AWK-29, W17): rename, duplicate, close for responses and reopen, archive, and the
/// summary the bar and the tabs show. Editors change; everyone in the organisation reads the summary.
/// </summary>
public sealed class SurveyMenuApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task<long> AuditCountAsync(AskwerkApiFactory factory, string action) =>
        SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM audit_log WHERE action = '{action}'").ContinueWith(task => (long)task.Result!, TaskScheduler.Default);

    [Fact]
    public async Task Summary_GivesTheBarItsStatusAndTheTabsTheirCounts()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal(("Customer satisfaction 2026", "published", 1), (summary.GetProperty("title").GetString(), summary.GetProperty("status").GetString(), summary.GetProperty("publishedVersion").GetInt32()));
        Assert.Equal((3, 0), (summary.GetProperty("responses").GetInt32(), summary.GetProperty("participants").GetInt32()));
        Assert.False(summary.GetProperty("hasChanges").GetBoolean());
    }

    [Fact]
    public async Task Rename_ChangesTheTitle_AndRefusesAnEmptyOne()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);

        var renamed = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/title"), new { title = "  Employee pulse Q4 " }, Cancel);
        var empty = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/title"), new { title = " " }, Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Equal("title-required", (await JsonAsync(empty)).GetProperty("errors").GetProperty("title")[0].GetString());
        Assert.Equal("Employee pulse Q4", summary.GetProperty("title").GetString());
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.renamed"));
    }

    [Fact]
    public async Task Duplicate_CopiesTheDraftIntoANewDraft()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var copied = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/duplicate"), new { title = "Copy of Customer satisfaction 2026" }, Cancel);
        var copyId = (await JsonAsync(copied)).GetProperty("id").GetString();
        var draft = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{copyId}/draft"), Cancel);
        var original = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/draft"), Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{copyId}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        Assert.Equal(original.GetProperty("definition").GetProperty("pages").GetRawText(), draft.GetProperty("definition").GetProperty("pages").GetRawText());
        Assert.Equal(("Copy of Customer satisfaction 2026", "draft", 0), (summary.GetProperty("title").GetString(), summary.GetProperty("status").GetString(), summary.GetProperty("responses").GetInt32()));
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.duplicated"));
    }

    [Fact]
    public async Task Close_StopsResponses_AndReopenTakesThemAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        using var respondent = await factory.CreateReadyClientAsync();

        var closed = await owner.PostAsync(Url($"/api/app/surveys/{id}/close"), null, Cancel);
        var whileClosed = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);
        var reopened = await owner.PostAsync(Url($"/api/app/surveys/{id}/reopen"), null, Cancel);
        var afterwards = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (closed.StatusCode, reopened.StatusCode));
        Assert.Equal("closed", whileClosed.GetProperty("survey").GetProperty("state").GetString());
        Assert.Equal("open", afterwards.GetProperty("survey").GetProperty("state").GetString());
        Assert.Equal((1L, 1L), (await AuditCountAsync(factory, "survey.closed"), await AuditCountAsync(factory, "survey.reopened")));
    }

    [Fact]
    public async Task Close_BeforePublishing_IsRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);

        var closed = await owner.PostAsync(Url($"/api/app/surveys/{id}/close"), null, Cancel);

        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal("not-published", (await JsonAsync(closed)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Archive_TakesTheSurveyOutOfTheList()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);

        var archived = await owner.PostAsync(Url($"/api/app/surveys/{id}/archive"), null, Cancel);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.archived"));
    }

    [Fact]
    public async Task Viewers_ReadTheSummary_ButDoNotChangeTheSurvey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");

        var summary = await owner.GetAsync(Url($"/api/app/surveys/{id}/summary"), Cancel);
        var renamed = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/title"), new { title = "Mine" }, Cancel);
        var copied = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/duplicate"), new { title = "Mine" }, Cancel);
        var closed = await owner.PostAsync(Url($"/api/app/surveys/{id}/close"), null, Cancel);
        var archived = await owner.PostAsync(Url($"/api/app/surveys/{id}/archive"), null, Cancel);

        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], [renamed.StatusCode, copied.StatusCode, closed.StatusCode, archived.StatusCode]);
    }
}
