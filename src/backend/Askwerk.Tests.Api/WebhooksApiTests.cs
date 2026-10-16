// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// Webhooks (AWK-32, C32 A): managed through the Public API with webhooks:manage and on the API page; each event is
/// posted signed, retried on failure, logged per delivery and can be replayed; a webhook failing for 3 days is paused.
/// </summary>
public sealed class WebhooksApiTests(PostgresServer server)
{
    private static readonly string[] Manage = ["webhooks:manage"];
    private static readonly string[] Published = ["survey.published"];
    private static readonly string[] ResponseAndExport = ["response.completed", "export.completed"];
    private static readonly string[] Unknown = ["survey.deleted"];
    private static readonly string[] AllFour = ["export.completed", "response.completed", "survey.published", "survey.closed"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    private static async Task<HttpClient> KeyClientAsync(AskwerkApiFactory factory, HttpClient owner, string[]? scopes = null)
    {
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner, scopes ?? Manage);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    private static async Task<(string Id, string Secret)> CreateWebhookAsync(HttpClient key, string[]? events = null)
    {
        var created = await key.PostAsJsonAsync(Url("/api/v1/webhooks"), new { url = "https://hr.acme.at/askwerk", events = events ?? AllFour, description = "HR sync" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);

        return (body.GetProperty("webhook").GetProperty("id").GetString()!, body.GetProperty("secret").GetString()!);
    }

    private static async Task<string> PublishedSurveyAsync(HttpClient owner)
    {
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return id;
    }

    [Fact]
    public async Task Create_ShowsTheSecretOnce_AndListsTheWebhook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyClientAsync(factory, owner);

        var (id, secret) = await CreateWebhookAsync(key);
        var list = await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/webhooks"), Cancel);

        Assert.StartsWith("whsec_", secret, StringComparison.Ordinal);
        var item = list.GetProperty("items")[0];
        Assert.Equal((id, "https://hr.acme.at/askwerk", true), (item.GetProperty("id").GetString(), item.GetProperty("url").GetString(), item.GetProperty("active").GetBoolean()));
        Assert.False(item.TryGetProperty("secret", out _));
    }

    [Fact]
    public async Task Create_RefusesPlainHttpAndUnknownEvents_AndKeysWithoutTheScope()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyClientAsync(factory, owner);
        using var reader = await KeyClientAsync(factory, owner, ["surveys:read"]);

        var http = await key.PostAsJsonAsync(Url("/api/v1/webhooks"), new { url = "http://hr.acme.at/askwerk", events = Published }, Cancel);
        var unknown = await key.PostAsJsonAsync(Url("/api/v1/webhooks"), new { url = "https://hr.acme.at/askwerk", events = Unknown }, Cancel);
        var noScope = await reader.GetAsync(Url("/api/v1/webhooks"), Cancel);

        Assert.Equal("https://askwerk.org/problems/url-invalid", (await JsonAsync(http)).GetProperty("type").GetString());
        Assert.Equal("https://askwerk.org/problems/events-invalid", (await JsonAsync(unknown)).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
    }

    [Fact]
    public async Task Publishing_PostsASignedEvent_ToTheSubscribedWebhook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyClientAsync(factory, owner);
        var (_, secret) = await CreateWebhookAsync(key, Published);

        var surveyId = await PublishedSurveyAsync(owner);
        await RunJobsAsync(factory);

        var (url, headers, body) = Assert.Single(factory.Webhooks.Received);
        Assert.Equal("https://hr.acme.at/askwerk", url.ToString());
        Assert.Equal("survey.published", headers["Askwerk-Event"]);
        var signature = headers["Askwerk-Signature"];
        var timestamp = signature.Split(',')[0]["t=".Length..];
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        Assert.Equal($"t={timestamp},v1={expected}", signature);
        var payload = JsonDocument.Parse(body).RootElement;
        Assert.Equal(("survey.published", surveyId, 1), (payload.GetProperty("type").GetString(), payload.GetProperty("data").GetProperty("surveyId").GetString(), payload.GetProperty("data").GetProperty("version").GetInt32()));
        Assert.StartsWith("evt_", payload.GetProperty("id").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedDelivery_IsRetried_LoggedAndCanBeReplayed()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyClientAsync(factory, owner);
        var (webhookId, _) = await CreateWebhookAsync(key, Published);
        factory.Webhooks.Answers.Enqueue(HttpStatusCode.ServiceUnavailable);

        await PublishedSurveyAsync(owner);
        await RunJobsAsync(factory);
        var afterFailure = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/webhooks/{webhookId}/deliveries"), Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE job SET run_at = now() WHERE done_at IS NULL");
        await RunJobsAsync(factory);
        var afterRetry = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/webhooks/{webhookId}/deliveries"), Cancel);
        var deliveryId = afterRetry.GetProperty("items")[0].GetProperty("id").GetString();
        var replay = await key.PostAsync(Url($"/api/v1/webhooks/{webhookId}/deliveries/{deliveryId}/replay"), null, Cancel);
        await RunJobsAsync(factory);

        var failed = afterFailure.GetProperty("items")[0];
        Assert.Equal(("pending", 1, 503), (failed.GetProperty("status").GetString(), failed.GetProperty("attempts").GetInt32(), failed.GetProperty("responseStatus").GetInt32()));
        Assert.Equal(("delivered", 2), (afterRetry.GetProperty("items")[0].GetProperty("status").GetString(), afterRetry.GetProperty("items")[0].GetProperty("attempts").GetInt32()));
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(3, factory.Webhooks.Received.Count);
    }

    [Fact]
    public async Task AWebhookFailingFor3Days_IsPaused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyClientAsync(factory, owner);
        var (webhookId, _) = await CreateWebhookAsync(key, Published);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE webhook SET failing_since = now() - interval '4 days'");
        factory.Webhooks.Answers.Enqueue(HttpStatusCode.InternalServerError);

        await PublishedSurveyAsync(owner);
        await RunJobsAsync(factory);
        var webhook = (await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/webhooks"), Cancel)).GetProperty("items")[0];
        var resumed = await key.PatchAsJsonAsync(Url($"/api/v1/webhooks/{webhookId}"), new { active = true }, Cancel);

        Assert.Equal((false, true), (webhook.GetProperty("active").GetBoolean(), webhook.GetProperty("paused").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.True((await JsonAsync(resumed)).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task ResponsesAndExports_RaiseTheirEvents()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyClientAsync(factory, owner);
        await CreateWebhookAsync(key, ResponseAndExport);
        var surveyId = await PublishedSurveyAsync(owner);
        using var respondent = await factory.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{surveyId}/response"), new { language = "de" }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{surveyId}/response/pages/p1"), new { answers = new { q1 = new { number = 4 } } }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{surveyId}/response/pages/p2"), new { answers = new { q3 = new { option = "1" } } }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PostAsync(Url($"/api/app/run/{surveyId}/response/complete"), null, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync(Url($"/api/app/surveys/{surveyId}/exports"), new { format = "csv" }, Cancel)).EnsureSuccessStatusCode();

        await RunJobsAsync(factory);
        await RunJobsAsync(factory);

        var types = factory.Webhooks.Received.Select(item => JsonDocument.Parse(item.Body).RootElement.GetProperty("type").GetString()).Order().ToList();
        Assert.Equal(["export.completed", "response.completed"], types);
        var export = factory.Webhooks.Received.Select(item => JsonDocument.Parse(item.Body).RootElement).Single(item => item.GetProperty("type").GetString() == "export.completed");
        Assert.EndsWith("/file", export.GetProperty("data").GetProperty("file").GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheApiPage_ManagesWebhooks_ForOwnersAndAdmins_AndSendsAPing()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var created = await owner.PostAsJsonAsync(Url("/api/app/webhooks"), new { url = "https://hr.acme.at/askwerk", events = AllFour }, Cancel);
        var id = (await JsonAsync(created)).GetProperty("webhook").GetProperty("id").GetString();
        var ping = await owner.PostAsync(Url($"/api/app/webhooks/{id}/ping"), null, Cancel);
        await RunJobsAsync(factory);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/webhooks"), Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");
        var editor = await owner.GetAsync(Url("/api/app/webhooks"), Cancel);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, ping.StatusCode);
        Assert.Equal("ping", JsonDocument.Parse(Assert.Single(factory.Webhooks.Received).Body).RootElement.GetProperty("type").GetString());
        Assert.Equal("delivered", list.GetProperty("items")[0].GetProperty("lastDelivery").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, editor.StatusCode);
        Assert.Equal(1L, (long)(await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'webhook.created'"))!);
    }
}
