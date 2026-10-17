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
/// Connections (W36, AWK-49): Connect a system sets up an OAuth client or API key and a webhook from its goals, shows
/// the secrets once, and the list says whether it works; pausing stops both directions, new secrets end the old ones,
/// removing revokes and deletes what was set up.
/// </summary>
public sealed class ConnectionsApiTests(PostgresServer server)
{
    private static readonly string[] AnswersAndPeople = ["answers", "people"];
    private static readonly string[] Answers = ["answers"];
    private static readonly string[] People = ["people"];
    private static readonly string[] Results = ["results"];
    private static readonly string[] ResponseCompleted = ["response.completed"];
    private static readonly string[] Published = ["survey.published"];
    private static readonly string[] Reading = ["surveys:read", "responses:read"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    private static HttpClient Bearer(AskwerkApiFactory factory, string secret)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    private static async Task<JsonElement> CrmAsync(HttpClient owner, string[]? events = null, string[]? surveyIds = null)
    {
        var created = await owner.PostAsJsonAsync(
            Url("/api/app/connections"),
            new { name = "Acme CRM", goals = AnswersAndPeople, webhookUrl = "https://crm.acme.at/hooks/askwerk", events = events ?? ResponseCompleted, surveyIds, signIn = "oauth", permissions = People, matchBy = "external-id" },
            Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return await JsonAsync(created);
    }

    private static async Task<JsonElement> NightlyAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync(Url("/api/app/connections"), new { name = "Power BI nightly", goals = Answers, signIn = "api-key", permissions = Results }, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return await JsonAsync(created);
    }

    private static async Task<string> PublishedSurveyAsync(HttpClient owner, string? id = null)
    {
        id ??= await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return id;
    }

    [Fact]
    public async Task ConnectingACrm_SetsUpAClientAndAWebhook_AndShowsTheSecretsOnce()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var created = await CrmAsync(owner);
        var credentials = created.GetProperty("credentials");
        var token = await OAuthApiTests.AccessTokenAsync(factory, credentials.GetProperty("clientId").GetString()!, credentials.GetProperty("clientSecret").GetString()!);
        using var crm = Bearer(factory, token);
        var surveys = await crm.GetAsync(Url("/api/v1/surveys"), Cancel);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/connections"), Cancel);

        Assert.StartsWith("whsec_", credentials.GetProperty("webhookSecret").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, credentials.GetProperty("apiKey").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, surveys.StatusCode);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(("Acme CRM", "external-id"), (item.GetProperty("name").GetString(), item.GetProperty("matchBy").GetString()));
        Assert.Equal(("oauth", "people"), (item.GetProperty("calls").GetProperty("signIn").GetString(), item.GetProperty("calls").GetProperty("permissions")[0].GetString()));
        Assert.Equal("https://crm.acme.at/hooks/askwerk", item.GetProperty("webhook").GetProperty("url").GetString());
        Assert.False(item.TryGetProperty("credentials", out _));
        Assert.DoesNotContain("acs_", list.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_IsWaitingUntilSomethingHappens_ThenWorks_AndTheTestEventIsSigned()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var created = await CrmAsync(owner);
        var id = created.GetProperty("connection").GetProperty("id").GetString();
        var secret = created.GetProperty("credentials").GetProperty("webhookSecret").GetString()!;

        var waiting = created.GetProperty("connection").GetProperty("health").GetString();
        var test = await owner.PostAsync(Url($"/api/app/connections/{id}/test"), null, Cancel);
        await RunJobsAsync(factory);
        var after = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/connections/{id}"), Cancel);

        Assert.Equal("waiting", waiting);
        Assert.Equal(HttpStatusCode.Accepted, test.StatusCode);
        var (_, headers, body) = Assert.Single(factory.Webhooks.Received);
        Assert.Equal("ping", headers["Askwerk-Event"]);
        var timestamp = headers["Askwerk-Signature"].Split(',')[0]["t=".Length..];
        Assert.EndsWith(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))), headers["Askwerk-Signature"], StringComparison.Ordinal);
        Assert.Equal(("works", 1), (after.GetProperty("health").GetString(), after.GetProperty("webhook").GetProperty("deliveredThisWeek").GetInt32()));
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("lastActivityAt").ValueKind);
        Assert.Equal(200, after.GetProperty("webhook").GetProperty("lastStatus").GetInt32());
        Assert.True(after.GetProperty("webhook").GetProperty("lastDurationMs").GetInt32() >= 0);
        Assert.Equal(1, after.GetProperty("days")[6].GetProperty("ok").GetInt32());
    }

    [Fact]
    public async Task TheWebhook_GetsOnlyTheChosenSurveys()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var chosen = await SurveysApiTests.CreateSurveyAsync(owner);
        await CrmAsync(owner, Published, [chosen]);

        await PublishedSurveyAsync(owner);
        await PublishedSurveyAsync(owner, chosen);
        await RunJobsAsync(factory);

        var (_, _, body) = Assert.Single(factory.Webhooks.Received);
        Assert.Equal(chosen, JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("surveyId").GetString());
    }

    [Fact]
    public async Task ANightlyExport_GetsAReadOnlyKey_ThatWorks()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var created = await NightlyAsync(owner);
        using var bi = Bearer(factory, created.GetProperty("credentials").GetProperty("apiKey").GetString()!);
        var surveys = await bi.GetAsync(Url("/api/v1/surveys"), Cancel);
        var view = created.GetProperty("connection");

        Assert.Equal(HttpStatusCode.OK, surveys.StatusCode);
        Assert.Equal(("api-key", "results"), (view.GetProperty("calls").GetProperty("signIn").GetString(), view.GetProperty("calls").GetProperty("permissions")[0].GetString()));
        Assert.Equal(JsonValueKind.Null, view.GetProperty("webhook").ValueKind);
    }

    [Fact]
    public async Task Pausing_StopsBothDirections_AndResumingStartsThemAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var crm = await CrmAsync(owner, Published);
        var crmId = crm.GetProperty("connection").GetProperty("id").GetString();
        var credentials = crm.GetProperty("credentials");
        var token = await OAuthApiTests.AccessTokenAsync(factory, credentials.GetProperty("clientId").GetString()!, credentials.GetProperty("clientSecret").GetString()!);
        var nightly = await NightlyAsync(owner);
        var nightlyId = nightly.GetProperty("connection").GetProperty("id").GetString();
        using var key = Bearer(factory, nightly.GetProperty("credentials").GetProperty("apiKey").GetString()!);
        using var tokenClient = Bearer(factory, token);

        var paused = await JsonAsync(await owner.PutAsJsonAsync(Url($"/api/app/connections/{crmId}/paused"), new { paused = true }, Cancel));
        (await owner.PutAsJsonAsync(Url($"/api/app/connections/{nightlyId}/paused"), new { paused = true }, Cancel)).EnsureSuccessStatusCode();
        var keyWhilePaused = await key.GetAsync(Url("/api/v1/surveys"), Cancel);
        var tokenWhilePaused = await tokenClient.GetAsync(Url("/api/v1/surveys"), Cancel);
        await PublishedSurveyAsync(owner);
        await RunJobsAsync(factory);
        var receivedWhilePaused = factory.Webhooks.Received.Count;
        (await owner.PutAsJsonAsync(Url($"/api/app/connections/{nightlyId}/paused"), new { paused = false }, Cancel)).EnsureSuccessStatusCode();
        var keyAfter = await key.GetAsync(Url("/api/v1/surveys"), Cancel);
        var counted = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/connections/{nightlyId}"), Cancel);

        Assert.Equal("paused", paused.GetProperty("health").GetString());
        Assert.False(string.IsNullOrEmpty(paused.GetProperty("pausedBy").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, keyWhilePaused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenWhilePaused.StatusCode);
        Assert.Equal(0, receivedWhilePaused);
        Assert.Equal(HttpStatusCode.OK, keyAfter.StatusCode);
        Assert.Equal(("warning", "calls-refused", 1), (counted.GetProperty("health").GetString(), counted.GetProperty("reason").GetString(), counted.GetProperty("refusedThisWeek").GetInt32()));
        var today = counted.GetProperty("days")[6];
        Assert.Equal((1, 1), (today.GetProperty("ok").GetInt32(), today.GetProperty("failed").GetInt32()));
    }

    [Fact]
    public async Task NewSecrets_EndTheOldOnes()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var nightly = await NightlyAsync(owner);
        var id = nightly.GetProperty("connection").GetProperty("id").GetString();
        using var old = Bearer(factory, nightly.GetProperty("credentials").GetProperty("apiKey").GetString()!);

        var renewed = await JsonAsync(await owner.PostAsync(Url($"/api/app/connections/{id}/secrets"), null, Cancel));
        using var fresh = Bearer(factory, renewed.GetProperty("apiKey").GetString()!);

        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync(Url("/api/v1/surveys"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync(Url("/api/v1/surveys"), Cancel)).StatusCode);
    }

    [Fact]
    public async Task Removing_RevokesTheClient_AndDeletesTheWebhook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var crm = await CrmAsync(owner);
        var id = crm.GetProperty("connection").GetProperty("id").GetString();

        var removed = await owner.DeleteAsync(Url($"/api/app/connections/{id}"), Cancel);
        var connections = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/connections"), Cancel);
        var webhooks = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/webhooks"), Cancel);
        var clients = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/oauth-clients"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, connections.GetProperty("items").GetArrayLength());
        Assert.Equal(0, webhooks.GetProperty("items").GetArrayLength());
        Assert.Equal(0, clients.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Health_WarnsBeforeTheKeyEnds()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = (await NightlyAsync(owner)).GetProperty("connection").GetProperty("id").GetString();

        await SurveysApiTests.ScalarAsync(factory, "UPDATE api_key SET expires_at = now() + interval '5 days', last_used_at = now()");
        var ending = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/connections/{id}"), Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE api_key SET expires_at = now() - interval '1 day'");
        var ended = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/connections/{id}"), Cancel);

        Assert.Equal(("warning", "credentials-ending"), (ending.GetProperty("health").GetString(), ending.GetProperty("reason").GetString()));
        Assert.Equal(("failing", "credentials-ended"), (ended.GetProperty("health").GetString(), ended.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Create_RefusesNoGoals_CallsWithoutSignIn_AndOtherRoles()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var noGoals = await owner.PostAsJsonAsync(Url("/api/app/connections"), new { name = "CRM", goals = Array.Empty<string>() }, Cancel);
        var noSignIn = await owner.PostAsJsonAsync(Url("/api/app/connections"), new { name = "HR", goals = People, permissions = People }, Cancel);
        var badUrl = await owner.PostAsJsonAsync(Url("/api/app/connections"), new { name = "CRM", goals = Answers, webhookUrl = "http://crm.acme.at" }, Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");
        var editor = await owner.GetAsync(Url("/api/app/connections"), Cancel);

        Assert.Contains("goals-invalid", await noGoals.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Contains("sign-in-invalid", await noSignIn.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Contains("url-invalid", await badUrl.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, editor.StatusCode);
    }
    [Fact]
    public async Task TheCrm_SendsPeople_MatchedByExternalIdThenEmail()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await PublishedSurveyAsync(owner);
        var credentials = (await CrmAsync(owner)).GetProperty("credentials");
        using var crm = Bearer(factory, await OAuthApiTests.AccessTokenAsync(factory, credentials.GetProperty("clientId").GetString()!, credentials.GetProperty("clientSecret").GetString()!));

        var first = await JsonAsync(await crm.PostAsJsonAsync(Url($"/api/v1/surveys/{surveyId}/participants"), new { send = false, participants = new object[] { new { email = "Lena@acme.at", name = "Lena", externalId = "CRM-10442", fields = new Dictionary<string, string> { ["department"] = "Sales" } }, new { email = "no address" } } }, Cancel));
        var moved = await JsonAsync(await crm.PostAsJsonAsync(Url($"/api/v1/surveys/{surveyId}/participants"), new { send = false, participants = new object[] { new { email = "lena.huber@acme.at", externalId = "CRM-10442" } } }, Cancel));
        var found = await crm.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{surveyId}/participants?externalId=CRM-10442"), Cancel);

        Assert.Equal((1, 0, 1), (first.GetProperty("invited").GetInt32(), first.GetProperty("updated").GetInt32(), first.GetProperty("skipped").GetInt32()));
        Assert.Equal((0, 1), (moved.GetProperty("invited").GetInt32(), moved.GetProperty("updated").GetInt32()));
        Assert.Equal(first.GetProperty("participants")[0].GetProperty("link").GetString(), moved.GetProperty("participants")[0].GetProperty("link").GetString());
        var person = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(("lena.huber@acme.at", "Lena", "Sales", "waiting"), (person.GetProperty("email").GetString(), person.GetProperty("name").GetString(), person.GetProperty("fields").GetProperty("department").GetString(), person.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task MatchingByEmailOnly_AddsAPersonWithANewAddress_AndKeysWithoutTheScopeAreRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await PublishedSurveyAsync(owner);
        var created = await owner.PostAsJsonAsync(Url("/api/app/connections"), new { name = "HR", goals = People, signIn = "api-key", permissions = People, matchBy = "email" }, Cancel);
        using var hr = Bearer(factory, (await JsonAsync(created)).GetProperty("credentials").GetProperty("apiKey").GetString()!);
        using var bi = Bearer(factory, (await NightlyAsync(owner)).GetProperty("credentials").GetProperty("apiKey").GetString()!);

        await hr.PostAsJsonAsync(Url($"/api/v1/surveys/{surveyId}/participants"), new { send = false, participants = new object[] { new { email = "lena@acme.at", externalId = "P-1" } } }, Cancel);
        var second = await JsonAsync(await hr.PostAsJsonAsync(Url($"/api/v1/surveys/{surveyId}/participants"), new { send = false, participants = new object[] { new { email = "lena.huber@acme.at", externalId = "P-1" } } }, Cancel));
        var refused = await bi.PostAsJsonAsync(Url($"/api/v1/surveys/{surveyId}/participants"), new { participants = new object[] { new { email = "a@acme.at" } } }, Cancel);

        Assert.Equal(1, second.GetProperty("invited").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task AKeyMadeBeforeConnections_BecomesAConnection_Once()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (_, keyId) = await ApiKeysApiTests.CreateKeyAsync(owner, Reading);

        var made = await owner.PostAsJsonAsync(Url("/api/app/connections/adopt"), new { kind = "api-key", id = keyId }, Cancel);
        var again = await owner.PostAsJsonAsync(Url("/api/app/connections/adopt"), new { kind = "api-key", id = keyId }, Cancel);
        var view = await JsonAsync(made);

        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        Assert.Equal(("answers", "api-key"), (view.GetProperty("goals")[0].GetString(), view.GetProperty("calls").GetProperty("signIn").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}
