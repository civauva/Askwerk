// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Results of a survey (AWK-24, W11): an overview per question, the responses newest first on a cursor, and one
/// response with all its answers. Answers come from <see cref="RunnerApiTests.Definition"/>.
/// </summary>
public sealed class ResultsApiTests(PostgresServer server)
{
    private static readonly string[] SearchAndOther = ["1", "OTHER"];
    private static readonly string[] Search = ["1"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>A published survey with two complete responses and one partial one.</summary>
    internal static async Task<(HttpClient Owner, string SurveyId)> AnsweredAsync(AskwerkApiFactory factory)
    {
        var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        await RespondAsync(factory, id, "de", new { q1 = new { number = 4 }, q2 = new { options = SearchAndOther, other = "Messe" } }, new { q3 = new { option = "1" } });
        await RespondAsync(factory, id, "en", new { q1 = new { number = 5 }, q2 = new { options = Search } }, new { q3 = new { option = "0" }, q4 = new { text = "anna@acme.at" } });
        await RespondAsync(factory, id, "en", new { q1 = new { number = 2 } }, page2: null);

        return (owner, id);
    }

    private static async Task RespondAsync(AskwerkApiFactory factory, string id, string language, object page1, object? page2)
    {
        using var respondent = await factory.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = page1 }, Cancel)).EnsureSuccessStatusCode();

        if (page2 is not null)
        {
            (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p2"), new { answers = page2 }, Cancel)).EnsureSuccessStatusCode();
            (await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel)).EnsureSuccessStatusCode();
        }
    }

    private static JsonElement Question(JsonElement results, string variable) =>
        results.GetProperty("questions").EnumerateArray().Single(question => question.GetProperty("variable").GetString() == variable);

    private static Dictionary<string, int> Counts(JsonElement options) =>
        options.EnumerateArray().ToDictionary(option => option.GetProperty("label").GetString()!, option => option.GetProperty("count").GetInt32());

    [Fact]
    public async Task Overview_SummarisesEveryQuestion_InTheSurveysOrder()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;

        var results = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/results?language=en"), Cancel);

        Assert.Equal(("Customer satisfaction 2026", "de"), (results.GetProperty("title").GetString(), results.GetProperty("defaultLanguage").GetString()));
        var totals = results.GetProperty("totals");
        Assert.Equal((3, 2, 1), (totals.GetProperty("responses").GetInt32(), totals.GetProperty("complete").GetInt32(), totals.GetProperty("partial").GetInt32()));
        Assert.Equal(["SAT", "SOURCE", "BEFORE", "EMAIL"], results.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("variable").GetString()));

        var satisfaction = Question(results, "SAT");
        Assert.Equal(("Satisfied?", 3), (satisfaction.GetProperty("text").GetString(), satisfaction.GetProperty("answered").GetInt32()));
        Assert.Equal(3.67, Math.Round(satisfaction.GetProperty("numbers").GetProperty("average").GetDouble(), 2));
        Assert.Equal(
            [(1.0, 0), (2.0, 1), (3.0, 0), (4.0, 1), (5.0, 1)],
            satisfaction.GetProperty("distribution").EnumerateArray().Select(value => (value.GetProperty("value").GetDouble(), value.GetProperty("count").GetInt32())));

        Assert.Equal(new Dictionary<string, int> { ["Search"] = 2, ["Ads"] = 0, ["Other:"] = 1 }, Counts(Question(results, "SOURCE").GetProperty("options")));
        Assert.Equal(new Dictionary<string, int> { ["Yes"] = 1, ["No"] = 1 }, Counts(Question(results, "BEFORE").GetProperty("options")));
        Assert.Equal(["anna@acme.at"], Question(results, "EMAIL").GetProperty("latest").EnumerateArray().Select(text => text.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Overview_FiltersByStatus_ButTheTotalsStayWhole()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;

        var complete = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/results?status=complete"), Cancel);

        Assert.Equal(2, Question(complete, "SAT").GetProperty("answered").GetInt32());
        Assert.Equal(3, complete.GetProperty("totals").GetProperty("responses").GetInt32());
        Assert.Equal("Zufrieden?", Question(complete, "SAT").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Responses_NewestFirst_OnACursor_WithAnswersAsText()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;

        var first = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses?limit=2&language=en"), Cancel);
        var next = first.GetProperty("next").GetString();
        var second = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses?limit=2&language=en&cursor={Uri.EscapeDataString(next!)}"), Cancel);

        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(["SAT", "SOURCE", "BEFORE", "EMAIL"], first.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("variable").GetString()));
        var rows = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToList();
        Assert.Equal(3, rows.Select(row => row.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("next").ValueKind);
        Assert.Equal(("partial", "2"), (rows[0].GetProperty("status").GetString(), rows[0].GetProperty("answers").GetProperty("SAT").GetString()));
        Assert.Equal("Search, Other: Messe", rows[2].GetProperty("answers").GetProperty("SOURCE").GetString());
        Assert.Equal("Yes", rows[2].GetProperty("answers").GetProperty("BEFORE").GetString());
    }

    [Fact]
    public async Task Responses_OldestFirst_OnTheSameCursor()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;

        var first = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses?order=oldest&limit=2&language=en"), Cancel);
        var next = first.GetProperty("next").GetString();
        var second = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses?order=oldest&limit=2&language=en&cursor={Uri.EscapeDataString(next!)}"), Cancel);

        var rows = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToList();
        Assert.Equal(["4", "5", "2"], rows.Select(row => row.GetProperty("answers").GetProperty("SAT").GetString()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("next").ValueKind);
    }

    [Fact]
    public async Task Responses_FilterByStatus_AndSearchTheTypedAnswers()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;

        var partial = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses?status=partial"), Cancel);
        var found = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses?search=messe"), Cancel);

        Assert.Equal(1, partial.GetProperty("total").GetInt32());
        Assert.Equal("partial", partial.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(1, found.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task OneResponse_ShowsEveryQuestion_InTheLanguageItWasAnsweredIn()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;
        var list = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses"), Cancel);
        var oldest = list.GetProperty("items")[2].GetProperty("id").GetString();

        var response = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/responses/{oldest}"), Cancel);

        Assert.Equal(("complete", "de", 1), (response.GetProperty("status").GetString(), response.GetProperty("language").GetString(), response.GetProperty("version").GetInt32()));
        Assert.Equal(
            [("Zufrieden?", "4"), ("Woher?", "Suche, Sonstiges: Messe"), ("Schon hier?", "Ja"), ("E-Mail", null)],
            response.GetProperty("answers").EnumerateArray().Select(answer => (answer.GetProperty("text").GetString(), answer.GetProperty("value").GetString())));
    }

    [Fact]
    public async Task SurveyList_CountsTheResponses()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await AnsweredAsync(factory);
        using var _ = owner;

        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);

        Assert.Equal(3, list.GetProperty("items").EnumerateArray().Single(survey => survey.GetProperty("id").GetString() == id).GetProperty("responses").GetInt32());
    }

    [Fact]
    public async Task Results_AreForTheOrganisationOnly()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        using var stranger = await factory.CreateReadyClientAsync();

        var anonymous = await stranger.GetAsync(Url($"/api/app/surveys/{id}/results"), Cancel);
        var unknown = await owner.GetAsync(Url($"/api/app/surveys/{Guid.NewGuid()}/responses"), Cancel);
        var unknownResponse = await owner.GetAsync(Url($"/api/app/surveys/{id}/responses/{Guid.NewGuid()}"), Cancel);
        var draftOnly = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/results"), Cancel);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (anonymous.StatusCode, unknown.StatusCode, unknownResponse.StatusCode));
        Assert.Equal(0, draftOnly.GetProperty("totals").GetProperty("responses").GetInt32());
        Assert.False(draftOnly.GetProperty("published").GetBoolean());
    }
}
