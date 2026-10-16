// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Embedding the designer and Results in a host product (AWK-13, W21): embedded sessions from a hand-over token,
/// kept inside their survey; frames only on the allowed websites; the runner narrowed per survey; identified
/// respondents.
/// </summary>
public sealed class EmbeddingApiTests(PostgresServer server)
{
    private static readonly string[] HostSite = ["https://hr.acme.at"];
    private static readonly string[] TwoSites = ["https://www.acme.at", "https://intranet.acme.at"];
    private static readonly string[] Intranet = ["https://intranet.acme.at"];
    private static readonly string[] Elsewhere = ["https://evil.example.org"];
    private static readonly string[] Editors = ["HR-Editors"];
    private static readonly string[] NoGroups = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<(string Audience, string SurveyId)> SetUpAsync(HttpClient owner, RSA key, bool designer = true)
    {
        var audience = await HandoverApiTests.SetUpAsync(owner, key);
        (await owner.PutAsJsonAsync(
            Url("/api/app/sign-in"),
            new { handoverEnabled = true, issuer = "https://hr.example.org", keys = new[] { key.ExportSubjectPublicKeyInfoPem() }, provisioningEnabled = false, roleRules = new[] { new { group = "HR-Editors", role = "editor" } }, defaultRole = "viewer" },
            Cancel)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync(Url("/api/app/embedding"), new { origins = TwoSites, designerOrigins = designer ? HostSite : [] }, Cancel)).EnsureSuccessStatusCode();
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return (audience, id);
    }

    private static async Task<HttpResponseMessage> StartAsync(AskwerkApiFactory factory, string token, string view = "designer")
    {
        using var frame = factory.CreateClient();

        return await frame.PostAsJsonAsync(Url("/api/app/embed/session"), new { token, view }, Cancel);
    }

    [Fact]
    public async Task AnEmbeddedSession_OpensItsSurvey_AndNothingElse()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = RSA.Create(2048);
        var (audience, id) = await SetUpAsync(owner, key);
        var other = await SurveysApiTests.CreateSurveyAsync(owner, "Another");

        var started = await StartAsync(factory, HandoverApiTests.Token(key, audience, "u-5", "eva@acme.at", Editors, survey: id, locale: "de"));
        var session = await started.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        using var frame = factory.CreateClient();
        frame.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.StartsWith("aws_", session.GetProperty("token").GetString(), StringComparison.Ordinal);
        Assert.Equal(("designer", "de", id), (session.GetProperty("view").GetString(), session.GetProperty("language").GetString(), session.GetProperty("surveyId").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await frame.GetAsync(Url($"/api/app/surveys/{id}/draft"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await frame.GetAsync(Url("/api/app/me"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await frame.GetAsync(Url($"/api/app/surveys/{other}/draft"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await frame.GetAsync(Url("/api/app/surveys"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await frame.GetAsync(Url("/api/app/members"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await frame.PostAsync(Url("/api/app/embed/session/renew"), null, Cancel)).StatusCode);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'embed.session-started'"));
    }

    [Fact]
    public async Task EmbeddedSessions_AreRefused_WhenOffUsedOrNotLetIn_AndViewersGetResults()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = RSA.Create(2048);
        var (audience, id) = await SetUpAsync(owner, key, designer: false);
        var token = HandoverApiTests.Token(key, audience, "u-6", "fritz@acme.at", NoGroups, survey: id);

        var off = await StartAsync(factory, token);
        (await owner.PutAsJsonAsync(Url("/api/app/embedding"), new { origins = TwoSites, designerOrigins = HostSite }, Cancel)).EnsureSuccessStatusCode();
        var viewer = await StartAsync(factory, token);
        var used = await StartAsync(factory, token);
        var noSurvey = await StartAsync(factory, HandoverApiTests.Token(key, audience, "u-6", "fritz@acme.at", NoGroups));

        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.Contains("embedding-off", await off.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal("results", (await viewer.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("view").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, used.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noSurvey.StatusCode);
    }

    [Fact]
    public async Task EmbedPages_MayBeFramedOnlyByTheAllowedSites_AndTheRunnerIsNarrowedPerSurvey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = RSA.Create(2048);
        var (audience, id) = await SetUpAsync(owner, key);
        using var visitor = factory.CreateClient();

        var page = await visitor.GetAsync(Url($"/embed/designer?token={HandoverApiTests.Token(key, audience, "u-7", "g@acme.at", Editors, survey: id)}"), Cancel);
        var noToken = await visitor.GetAsync(Url("/embed/designer"), Cancel);
        var outside = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/embed-origins"), new { origins = Elsewhere }, Cancel);
        var narrowed = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/embed-origins"), new { origins = Intranet }, Cancel);
        var runner = await visitor.GetAsync(Url($"/s/{id}"), Cancel);
        var choice = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/embed-origins"), Cancel);

        Assert.Equal("frame-ancestors https://hr.acme.at", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("frame-ancestors 'none'", noToken.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, narrowed.StatusCode);
        Assert.Equal("frame-ancestors 'self' https://intranet.acme.at", runner.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(2, choice.GetProperty("organisation").GetArrayLength());
    }

    [Fact]
    public async Task AHostCanNameTheRespondent_AndTheResponseKeepsThem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = RSA.Create(2048);
        var (audience, id) = await SetUpAsync(owner, key);
        var identity = HandoverApiTests.Token(key, audience, "emp-77", "hanna@acme.at", NoGroups, survey: id);
        using var respondent = await factory.CreateReadyClientAsync();
        using var again = await factory.CreateReadyClientAsync();

        var started = await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en", identity }, Cancel);
        var reused = await again.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en", identity }, Cancel);
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner);
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var page = await api.GetFromJsonAsync<JsonElement>(Url($"/api/v1/surveys/{id}/responses"), Cancel);

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        var participant = page.GetProperty("items")[0].GetProperty("participant");
        Assert.Equal(("hanna@acme.at", "emp-77"), (participant.GetProperty("email").GetString(), participant.GetProperty("externalId").GetString()));
    }
}
