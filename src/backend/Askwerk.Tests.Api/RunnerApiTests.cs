// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>The runner's side of the App API (AWK-6, W8): respondents open a published survey, answer page by page, and finish.</summary>
public sealed class RunnerApiTests(PostgresServer server)
{
    private static readonly string[] SearchAndOther = ["1", "OTHER"];
    private static readonly string[] OnlyOther = ["OTHER"];
    private static readonly string[] AdsAndOther = ["2", "OTHER"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static JsonObject Text(string de, string en) => new() { ["de"] = de, ["en"] = en };

    /// <summary>Two pages: a required rating and multiple choice with "Other"; a required yes/no and an email.</summary>
    internal static JsonObject Definition() => new()
    {
        ["schemaVersion"] = 1,
        ["languages"] = new JsonArray("de", "en"),
        ["defaultLanguage"] = "de",
        ["layout"] = "one-at-a-time",
        ["welcome"] = new JsonObject { ["title"] = Text("Kundenzufriedenheit", "Customer satisfaction"), ["text"] = Text("Danke!", "Thank you!") },
        ["pages"] = new JsonArray(
            new JsonObject
            {
                ["id"] = "p1",
                ["questions"] = new JsonArray(
                    new JsonObject { ["id"] = "q1", ["variable"] = "SAT", ["type"] = "rating", ["text"] = Text("Zufrieden?", "Satisfied?"), ["required"] = true, ["scale"] = new JsonObject { ["min"] = 1, ["max"] = 5 } },
                    new JsonObject
                    {
                        ["id"] = "q2",
                        ["variable"] = "SOURCE",
                        ["type"] = "multiple",
                        ["text"] = Text("Woher?", "Where from?"),
                        ["required"] = false,
                        ["options"] = new JsonArray(new JsonObject { ["code"] = "1", ["label"] = Text("Suche", "Search") }, new JsonObject { ["code"] = "2", ["label"] = Text("Werbung", "Ads") }),
                        ["other"] = new JsonObject { ["code"] = "OTHER", ["label"] = Text("Sonstiges:", "Other:"), ["variable"] = "SOURCE_OTHER" },
                    }),
            },
            new JsonObject
            {
                ["id"] = "p2",
                ["questions"] = new JsonArray(
                    new JsonObject { ["id"] = "q3", ["variable"] = "BEFORE", ["type"] = "yes-no", ["text"] = Text("Schon hier?", "Been here?"), ["required"] = true },
                    new JsonObject { ["id"] = "q4", ["variable"] = "EMAIL", ["type"] = "short-text", ["format"] = "email", ["text"] = Text("E-Mail", "Email"), ["required"] = false }),
            }),
    };

    /// <summary>A published survey, and a fresh browser for the respondent (no owner cookies).</summary>
    private static async Task<(string SurveyId, HttpClient Respondent)> PublishedAsync(AskwerkApiFactory factory)
    {
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return (id, await factory.CreateReadyClientAsync());
    }

    private static Task<HttpResponseMessage> SavePageAsync(HttpClient client, string id, string page, object answers) =>
        client.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/{page}"), new { answers }, Cancel);

    [Fact]
    public async Task Open_PublishedSurvey_GivesRespondentsTheDefinition_NotTheInternalTitle()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);

        var opened = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);

        var survey = opened.GetProperty("survey");
        Assert.Equal(2, survey.GetProperty("definition").GetProperty("pages").GetArrayLength());
        Assert.Equal("one-at-a-time", survey.GetProperty("definition").GetProperty("layout").GetString());
        Assert.Equal("Acme GmbH", survey.GetProperty("organisation").GetString());
        Assert.False(survey.TryGetProperty("title", out _));
        Assert.Equal(JsonValueKind.Null, opened.GetProperty("response").ValueKind);
    }

    [Fact]
    public async Task Open_DraftOnlyOrUnknown_IsNotFound()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var draftOnly = await SurveysApiTests.CreateSurveyAsync(owner);
        using var respondent = await factory.CreateReadyClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await respondent.GetAsync(Url($"/api/app/run/{draftOnly}"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await respondent.GetAsync(Url($"/api/app/run/{Guid.NewGuid()}"), Cancel)).StatusCode);
    }

    [Fact]
    public async Task Answering_SavesPageByPage_AndResumesInTheSameBrowser()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);

        var started = await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel);
        var saved = await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 4 }, q2 = new { options = SearchAndOther, other = "trade fair" } });
        var resumed = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var response = resumed.GetProperty("response");
        Assert.Equal(("partial", "p2", "en"), (response.GetProperty("status").GetString(), response.GetProperty("pageId").GetString(), response.GetProperty("language").GetString()));
        Assert.Equal(4, response.GetProperty("answers").GetProperty("q1").GetProperty("number").GetInt32());
        Assert.Equal("trade fair", response.GetProperty("answers").GetProperty("q2").GetProperty("other").GetString());
    }

    [Fact]
    public async Task SavePage_ReportsMissingAndInvalidAnswersPerQuestion()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);
        await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "de" }, Cancel);

        var missing = await SavePageAsync(respondent, id, "p1", new { q2 = new { options = OnlyOther } });
        var invalid = await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 9 } });
        var email = await SavePageAsync(respondent, id, "p2", new { q3 = new { option = "1" }, q4 = new { text = "anna@acme" } });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var errors = (await JsonAsync(missing)).GetProperty("errors");
        Assert.Equal(("required", "other-text-missing"), (errors.GetProperty("q1")[0].GetString(), errors.GetProperty("q2")[0].GetString()));
        Assert.Equal("number-out-of-range", (await JsonAsync(invalid)).GetProperty("errors").GetProperty("q1")[0].GetString());
        Assert.Equal("format-email", (await JsonAsync(email)).GetProperty("errors").GetProperty("q4")[0].GetString());
    }

    [Fact]
    public async Task Complete_FinishesTheResponse_AndStoresTypedAnswers()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);
        await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "de" }, Cancel);
        await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 5 }, q2 = new { options = AdsAndOther, other = "Messe" } });
        await SavePageAsync(respondent, id, "p2", new { q3 = new { option = "0" }, q4 = new { text = "anna@acme.at" } });

        var completed = await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel);
        var afterwards = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);
        var changeAfterwards = await SavePageAsync(respondent, id, "p2", new { q3 = new { option = "1" } });

        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.Equal("complete", afterwards.GetProperty("response").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, changeAfterwards.StatusCode);
        Assert.Equal("5", (await SurveysApiTests.ScalarAsync(factory, "SELECT number_value::int::text FROM answer a JOIN survey_question q ON q.id = a.question_id WHERE q.variable_name = 'SAT'"))?.ToString());
        Assert.Equal("2,OTHER|Messe", await SurveysApiTests.ScalarAsync(factory, "SELECT array_to_string(option_codes, ',') || '|' || (detail->>'other') FROM answer a JOIN survey_question q ON q.id = a.question_id WHERE q.variable_name = 'SOURCE'"));
        Assert.Equal("complete", await SurveysApiTests.ScalarAsync(factory, "SELECT status FROM response"));
    }

    [Fact]
    public async Task Complete_WithRequiredAnswersMissing_IsRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);
        await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "de" }, Cancel);
        await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 3 } });

        var completed = await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, completed.StatusCode);
        Assert.Equal("required", (await JsonAsync(completed)).GetProperty("errors").GetProperty("q3")[0].GetString());
    }

    [Fact]
    public async Task SavePage_WithoutStarting_IsRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);

        var saved = await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 4 } });

        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal("not-started", (await JsonAsync(saved)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnotherBrowser_StartsItsOwnResponse()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, respondent) = await PublishedAsync(factory);
        using var second = await factory.CreateReadyClientAsync();
        await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "de" }, Cancel);
        await SavePageAsync(respondent, id, "p1", new { q1 = new { number = 4 } });

        var opened = await second.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);

        Assert.Equal(JsonValueKind.Null, opened.GetProperty("response").ValueKind);
    }

    [Fact]
    public async Task ShortCode_LeadsToThePublishedSurvey_ButNotToADraft()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        var draftOnly = await SurveysApiTests.CreateSurveyAsync(owner, "Not yet");
        using var respondent = await factory.CreateReadyClientAsync();
        var code = (string)(await SurveysApiTests.ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{id}'"))!;
        var draftCode = (string)(await SurveysApiTests.ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{draftOnly}'"))!;

        var found = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/code/{code}"), Cancel);
        var draft = await respondent.GetAsync(Url($"/api/app/run/code/{draftCode}"), Cancel);
        var unknown = await respondent.GetAsync(Url("/api/app/run/code/zzzzzzzz"), Cancel);

        Assert.Equal(id, found.GetProperty("surveyId").GetString());
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (draft.StatusCode, unknown.StatusCode));
    }
}
