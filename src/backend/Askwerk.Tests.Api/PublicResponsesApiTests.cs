// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Public API: responses changed since a sequence, and the codebook (AWK-14, C14 1), scope responses:read. What a
/// sync job needs to pull responses in long format and stay restart-safe.
/// </summary>
public sealed class PublicResponsesApiTests(PostgresServer server)
{
    private static readonly string[] SurveysOnly = ["surveys:read"];
    private static readonly string[] Ads = ["2"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<(string Id, HttpClient Owner)> PublishedAsync(AskwerkApiFactory factory)
    {
        var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return (id, owner);
    }

    private static async Task<HttpClient> RespondAsync(AskwerkApiFactory factory, string id, object page1, bool complete)
    {
        var respondent = await factory.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = page1 }, Cancel)).EnsureSuccessStatusCode();

        if (complete)
        {
            (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p2"), new { answers = new { q3 = new { option = "1" } } }, Cancel)).EnsureSuccessStatusCode();
            (await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel)).EnsureSuccessStatusCode();
        }

        return respondent;
    }

    private static async Task<HttpClient> KeyAsync(AskwerkApiFactory factory, HttpClient owner, string[]? scopes = null)
    {
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner, scopes);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    [Fact]
    public async Task Responses_SinceASequence_InLongFormat_AndThenNothingNew()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, owner) = await PublishedAsync(factory);
        using var _ = owner;
        using var first = await RespondAsync(factory, id, new { q1 = new { number = 4 }, q2 = new { options = Ads } }, complete: true);
        using var key = await KeyAsync(factory, owner);

        var page = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}/responses?since=0&language=en"), Cancel);
        var next = page.GetProperty("nextSince").GetInt64();
        var again = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}/responses?since={next}"), Cancel);

        var response = page.GetProperty("items")[0];
        Assert.Equal(("complete", 1, "en"), (response.GetProperty("status").GetString(), response.GetProperty("version").GetInt32(), response.GetProperty("language").GetString()));
        Assert.Equal(next, response.GetProperty("changeSequence").GetInt64());
        var answers = response.GetProperty("answers").EnumerateArray().Select(answer => (answer.GetProperty("variable").GetString(), answer.GetProperty("value").GetString(), answer.GetProperty("label").GetString())).ToList();
        Assert.Contains(("SAT", "4", "4"), answers);
        Assert.Contains(("SOURCE_2", "1", "Ads"), answers);
        Assert.Contains(("BEFORE", "1", "Yes"), answers);
        Assert.DoesNotContain(answers, answer => answer.Item1 == "SOURCE_1");
        Assert.Equal((0, next), (again.GetProperty("items").GetArrayLength(), again.GetProperty("nextSince").GetInt64()));
    }

    [Fact]
    public async Task AChangedResponse_ComesAgainWithAHigherSequence()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, owner) = await PublishedAsync(factory);
        using var _ = owner;
        using var respondent = await RespondAsync(factory, id, new { q1 = new { number = 2 } }, complete: false);
        using var key = await KeyAsync(factory, owner);
        var before = (await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}/responses?since=0"), Cancel)).GetProperty("nextSince").GetInt64();

        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 5 } } }, Cancel)).EnsureSuccessStatusCode();
        var after = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}/responses?since={before}"), Cancel);

        Assert.Equal(1, after.GetProperty("items").GetArrayLength());
        Assert.True(after.GetProperty("nextSince").GetInt64() > before);
        Assert.Equal("partial", after.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Codebook_HasEveryVariableWithItsValues()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (id, owner) = await PublishedAsync(factory);
        using var _ = owner;
        using var key = await KeyAsync(factory, owner);

        var codebook = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}/codebook?language=de"), Cancel);

        Assert.Equal((1, "de"), (codebook.GetProperty("version").GetInt32(), codebook.GetProperty("language").GetString()));
        var before = codebook.GetProperty("variables").EnumerateArray().Single(variable => variable.GetProperty("variable").GetString() == "BEFORE");
        Assert.Equal("Schon hier?", before.GetProperty("label").GetString());
        Assert.Contains(before.GetProperty("values").EnumerateArray(), value => value.GetProperty("code").GetString() == "1" && value.GetProperty("label").GetString() == "Ja");
    }

    [Fact]
    public async Task KeysWithoutResponsesRead_AreRefused_AndDraftsHaveNoCodebook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var draft = await SurveysApiTests.CreateSurveyAsync(owner);
        using var reader = await KeyAsync(factory, owner, SurveysOnly);
        using var key = await KeyAsync(factory, owner);

        var refused = await reader.GetAsync(Url($"/api/v1/surveys/{draft}/responses"), Cancel);
        var noCodebook = await key.GetAsync(Url($"/api/v1/surveys/{draft}/codebook"), Cancel);
        var missing = await key.GetAsync(Url($"/api/v1/surveys/{Guid.NewGuid()}/responses"), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, noCodebook.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
