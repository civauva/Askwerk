// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// The Public API, first endpoints (AWK-25, W14 part 2): API keys as Bearer tokens on /api/v1 only, scopes, a rate
/// limit per key, problem details with stable types, surveys on a cursor, and exports to start, poll and download.
/// </summary>
public sealed class PublicApiTests(PostgresServer server)
{
    private static readonly string[] SurveysRead = ["surveys:read"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    /// <summary>A client that calls with a new key of the owner's organisation, and nothing else.</summary>
    private static async Task<HttpClient> KeyClientAsync(AskwerkApiFactory factory, HttpClient owner, string[]? scopes = null)
    {
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner, scopes);

        return WithKey(factory, secret);
    }

    private static HttpClient WithKey(AskwerkApiFactory factory, string secret)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await JsonAsync(response);
        Assert.Equal(($"https://askwerk.org/problems/{type}", (int)status), (problem.GetProperty("type").GetString(), problem.GetProperty("status").GetInt32()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.Equal(32, problem.GetProperty("traceId").GetString()!.Length);

        return problem;
    }

    [Fact]
    public async Task Surveys_AreListedOnACursor_NewestChangeFirst()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, published) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        var draft = await SurveysApiTests.CreateSurveyAsync(owner, "Employee pulse Q4");
        using var api = await KeyClientAsync(factory, owner);

        var first = await api.GetFromJsonAsync<JsonElement>(Url("/api/v1/surveys?limit=1"), Cancel);
        var second = await api.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys?limit=1&cursor={Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!)}"), Cancel);
        var onlyPublished = await api.GetFromJsonAsync<JsonElement>(Url("/api/v1/surveys?status=published"), Cancel);

        Assert.Equal([draft], first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal([published], second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var survey = onlyPublished.GetProperty("items")[0];
        Assert.Equal(("published", 1, 3), (survey.GetProperty("status").GetString(), survey.GetProperty("publishedVersion").GetInt32(), survey.GetProperty("responses").GetInt32()));
        Assert.Equal(8, survey.GetProperty("code").GetString()!.Length);
        Assert.Equal(["de", "en"], survey.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
    }

    [Fact]
    public async Task Survey_IsReadWithItsVersionsAndLinks_AndOnlyInItsOrganisation()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        using var api = await KeyClientAsync(factory, owner);

        var survey = await api.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}"), Cancel);
        var unknown = await api.GetAsync(Url($"/api/v1/surveys/{Guid.CreateVersion7()}"), Cancel);

        Assert.Equal([1], survey.GetProperty("versions").EnumerateArray().Select(version => version.GetProperty("number").GetInt32()));
        Assert.EndsWith($"/s/{survey.GetProperty("code").GetString()}", survey.GetProperty("links")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal("de", survey.GetProperty("defaultLanguage").GetString());
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "survey-not-found");
    }

    [Fact]
    public async Task Calls_WithoutAUsableKey_AreUnauthorized()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (secret, keyId) = await ApiKeysApiTests.CreateKeyAsync(owner);
        var (expiring, expiringId) = await ApiKeysApiTests.CreateKeyAsync(owner);
        using var anonymous = factory.CreateClient();
        using var wrong = WithKey(factory, secret[..47] + (secret[47] == 'a' ? 'b' : 'a'));
        using var revoked = WithKey(factory, secret);
        using var expired = WithKey(factory, expiring);

        var none = await anonymous.GetAsync(Url("/api/v1/surveys"), Cancel);
        var bad = await wrong.GetAsync(Url("/api/v1/surveys"), Cancel);
        (await owner.DeleteAsync(Url($"/api/app/api-keys/{keyId}"), Cancel)).EnsureSuccessStatusCode();
        await SurveysApiTests.ScalarAsync(factory, $"UPDATE api_key SET expires_at = now() - interval '1 minute' WHERE id = '{expiringId}'");
        var afterRevoke = await revoked.GetAsync(Url("/api/v1/surveys"), Cancel);
        var afterExpiry = await expired.GetAsync(Url("/api/v1/surveys"), Cancel);

        await AssertProblemAsync(none, HttpStatusCode.Unauthorized, "unauthorized");
        await AssertProblemAsync(bad, HttpStatusCode.Unauthorized, "unauthorized");
        await AssertProblemAsync(afterRevoke, HttpStatusCode.Unauthorized, "unauthorized");
        await AssertProblemAsync(afterExpiry, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task CookieSessions_AreRefusedOnThePublicApi_AndKeysOnTheAppApi()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var api = await KeyClientAsync(factory, owner);

        var cookieOnPublic = await owner.GetAsync(Url("/api/v1/surveys"), Cancel);
        var keyOnApp = await api.GetAsync(Url("/api/app/surveys"), Cancel);

        await AssertProblemAsync(cookieOnPublic, HttpStatusCode.Unauthorized, "unauthorized");
        Assert.Equal(HttpStatusCode.Unauthorized, keyOnApp.StatusCode);
    }

    [Fact]
    public async Task AKeyWithoutTheScope_IsForbidden()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        using var api = await KeyClientAsync(factory, owner, SurveysRead);

        var export = await api.PostAsJsonAsync(Url($"/api/v1/surveys/{id}/exports"), new { format = "csv" }, Cancel);

        await AssertProblemAsync(export, HttpStatusCode.Forbidden, "scope-missing");
    }

    [Fact]
    public async Task Exports_AreStartedOnce_Polled_AndDownloaded()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        using var api = await KeyClientAsync(factory, owner);

        async Task<HttpResponseMessage> StartAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url($"/api/v1/surveys/{id}/exports")) { Content = JsonContent.Create(new { format = "csv", values = "codes" }) };
            request.Headers.Add("Idempotency-Key", "2c7e-weekly");
            return await api.SendAsync(request, Cancel);
        }

        var started = await StartAsync();
        var again = await StartAsync();
        var export = await JsonAsync(started);
        var exportId = export.GetProperty("id").GetString();
        var early = await api.GetAsync(Url($"/api/v1/exports/{exportId}/file"), Cancel);
        await RunJobsAsync(factory);
        var ready = await api.GetFromJsonAsync<JsonElement>(Url($"/api/v1/exports/{exportId}"), Cancel);
        var file = await api.GetAsync(Url($"/api/v1/exports/{exportId}/file"), Cancel);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal($"/api/v1/exports/{exportId}", started.Headers.Location?.OriginalString);
        Assert.Equal(("queued", id), (export.GetProperty("status").GetString(), export.GetProperty("surveyId").GetString()));
        Assert.Equal(exportId, (await JsonAsync(again)).GetProperty("id").GetString());
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM export"));
        await AssertProblemAsync(early, HttpStatusCode.Conflict, "export-not-ready");
        Assert.Equal("ready", ready.GetProperty("status").GetString());
        Assert.Equal((3, 3), (ready.GetProperty("progress").GetProperty("done").GetInt32(), ready.GetProperty("progress").GetProperty("total").GetInt32()));
        Assert.Equal(($"/api/v1/exports/{exportId}/file", "customer-satisfaction-2026.csv"), (ready.GetProperty("file").GetProperty("url").GetString(), ready.GetProperty("file").GetProperty("name").GetString()));
        Assert.StartsWith("response_id,status,", Encoding.UTF8.GetString(await file.Content.ReadAsByteArrayAsync(Cancel)).TrimStart('﻿'), StringComparison.Ordinal);
        Assert.Equal("ApiKey", await SurveysApiTests.ScalarAsync(factory, "SELECT actor_kind FROM audit_log WHERE action = 'export.downloaded'"));
        Assert.Equal("ApiKey", await SurveysApiTests.ScalarAsync(factory, "SELECT actor_kind FROM audit_log WHERE action = 'results.exported'"));
        Assert.NotNull(await SurveysApiTests.ScalarAsync(factory, "SELECT last_used_at FROM api_key"));
    }

    [Fact]
    public async Task Problems_SpeakTheCallersLanguage()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var draft = await SurveysApiTests.CreateSurveyAsync(owner);
        using var api = await KeyClientAsync(factory, owner);
        api.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-AT");

        var export = await api.PostAsJsonAsync(Url($"/api/v1/surveys/{draft}/exports"), new { format = "csv" }, Cancel);
        var unknown = await api.PostAsJsonAsync(Url($"/api/v1/surveys/{draft}/exports"), new { format = "sav" }, Cancel);

        var problem = await AssertProblemAsync(export, HttpStatusCode.Conflict, "not-published");
        Assert.Equal("Die Umfrage ist nicht veröffentlicht.", problem.GetProperty("title").GetString());
        await AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "unknown-format");
    }

    [Fact]
    public async Task EachKey_HasItsOwnRateLimit()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        factory.ApiRateLimit = 2;
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var first = await KeyClientAsync(factory, owner);
        using var second = await KeyClientAsync(factory, owner);

        var one = await first.GetAsync(Url("/api/v1/surveys"), Cancel);
        await first.GetAsync(Url("/api/v1/surveys"), Cancel);
        var limited = await first.GetAsync(Url("/api/v1/surveys"), Cancel);
        var other = await second.GetAsync(Url("/api/v1/surveys"), Cancel);

        Assert.Equal(("2", "1"), (one.Headers.GetValues("RateLimit-Limit").Single(), one.Headers.GetValues("RateLimit-Remaining").Single()));
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) is > 0 and <= 60);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
