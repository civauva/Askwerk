// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Logic on the server (AWK-31, W19): a question that is not shown is not required and its answer is not stored, and
/// a page skipped or after an ending is not needed to complete.
/// </summary>
public sealed class LogicRunApiTests(PostgresServer server)
{
    private static readonly string[] Search = ["1"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>q2 (required) shows only when q1 is at most 2; after p1 the survey ends when q1 is 5.</summary>
    private static JsonObject Definition()
    {
        var definition = RunnerApiTests.Definition();
        var p1 = definition["pages"]![0]!.AsObject();
        p1["questions"]![1]!["required"] = true;
        p1["questions"]![1]!["showWhen"] = new JsonObject { ["match"] = "all", ["conditions"] = new JsonArray(new JsonObject { ["question"] = "q1", ["op"] = "at-most", ["number"] = 2 }) };
        p1["after"] = new JsonObject { ["when"] = new JsonObject { ["match"] = "all", ["conditions"] = new JsonArray(new JsonObject { ["question"] = "q1", ["op"] = "is", ["number"] = 5 }) }, ["end"] = true };

        return definition;
    }

    private static async Task<(string Id, HttpClient Respondent)> StartedAsync(AskwerkApiFactory factory)
    {
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        var respondent = await factory.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();

        return (id, respondent);
    }

    private static Task<HttpResponseMessage> SavePageAsync(HttpClient client, string id, string page, object answers) =>
        client.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/{page}"), new { answers }, Cancel);

    [Fact]
    public async Task AHiddenRequiredQuestion_IsNotRequired_AndItsAnswerIsNotStored()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await StartedAsync(factory);
        using var _ = respondent;

        var saved = await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 4 }, q2 = new { options = Search } });
        var shownAndMissing = await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 1 } });

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(1L, (long)(await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM answer"))!);
        Assert.Equal(HttpStatusCode.BadRequest, shownAndMissing.StatusCode);
    }

    [Fact]
    public async Task AnEnding_LetsTheResponseComplete_WithoutTheLaterPages()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await StartedAsync(factory);
        using var _ = respondent;
        (await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 5 } })).EnsureSuccessStatusCode();

        var completed = await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
    }

    [Fact]
    public async Task WithoutTheEnding_TheLaterRequiredQuestionsStillCount()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await StartedAsync(factory);
        using var _ = respondent;
        (await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 4 } })).EnsureSuccessStatusCode();

        var completed = await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, completed.StatusCode);
    }
}
