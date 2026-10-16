// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// OAuth 2.0 client credentials (AWK-32, C32 B): clients are created on the API page; /api/v1/oauth/token gives an
/// opaque access token for 15 minutes that works like a key; revoking the client ends its tokens at once.
/// </summary>
public sealed class OAuthApiTests(PostgresServer server)
{
    private static readonly string[] ReadResults = ["surveys:read", "responses:read"];
    private static readonly string[] Published = ["survey.published"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    internal static async Task<(string ClientId, string Secret, string Id)> CreateClientAsync(HttpClient owner, string[]? scopes = null)
    {
        var created = await owner.PostAsJsonAsync(Url("/api/app/oauth-clients"), new { name = "HR system", scopes = scopes ?? ReadResults, expiresInDays = 365 }, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);

        return (body.GetProperty("client").GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!, body.GetProperty("client").GetProperty("id").GetString()!);
    }

    private static async Task<HttpResponseMessage> TokenAsync(HttpClient client, Dictionary<string, string> form, (string Id, string Secret)? basic = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/oauth/token")) { Content = new FormUrlEncodedContent(form) };

        if (basic is { } credentials)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Id}:{credentials.Secret}")));
        }

        return await client.SendAsync(request, Cancel);
    }

    internal static async Task<string> AccessTokenAsync(AskwerkApiFactory factory, string clientId, string secret, string? scope = null)
    {
        using var anonymous = factory.CreateClient();
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret };

        if (scope is not null)
        {
            form["scope"] = scope;
        }

        var answer = await TokenAsync(anonymous, form);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        return (await JsonAsync(answer)).GetProperty("access_token").GetString()!;
    }

    private static HttpClient WithToken(AskwerkApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    [Fact]
    public async Task Token_WithBasicCredentials_WorksLikeAKey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, secret, _) = await CreateClientAsync(owner);
        using var anonymous = factory.CreateClient();

        var answer = await TokenAsync(anonymous, new() { ["grant_type"] = "client_credentials" }, (clientId, secret));
        var token = await JsonAsync(answer);
        using var caller = WithToken(factory, token.GetProperty("access_token").GetString()!);
        var surveys = await caller.GetAsync(Url("/api/v1/surveys"), Cancel);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("no-store", answer.Headers.CacheControl?.ToString());
        Assert.Equal(("Bearer", 900, "surveys:read responses:read"), (token.GetProperty("token_type").GetString(), token.GetProperty("expires_in").GetInt32(), token.GetProperty("scope").GetString()));
        Assert.StartsWith("awt_", token.GetProperty("access_token").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, surveys.StatusCode);
    }

    [Fact]
    public async Task Token_NarrowsToTheScopesAskedFor_AndRefusesOthers()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, secret, _) = await CreateClientAsync(owner);
        using var anonymous = factory.CreateClient();

        var narrowed = await TokenAsync(anonymous, new() { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret, ["scope"] = "surveys:read" });
        var tooMuch = await TokenAsync(anonymous, new() { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret, ["scope"] = "webhooks:manage" });
        var narrowedToken = await JsonAsync(narrowed);
        using var caller = WithToken(factory, narrowedToken.GetProperty("access_token").GetString()!);
        var export = await caller.PostAsJsonAsync(Url($"/api/v1/surveys/{Guid.NewGuid()}/exports"), new { format = "csv" }, Cancel);

        Assert.Equal("surveys:read", narrowedToken.GetProperty("scope").GetString());
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_scope"), (tooMuch.StatusCode, (await JsonAsync(tooMuch)).GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Fact]
    public async Task Token_RefusesAWrongSecret_AndOtherGrants()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, _, _) = await CreateClientAsync(owner);
        using var anonymous = factory.CreateClient();

        var wrong = await TokenAsync(anonymous, new() { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = "acs_wrong" });
        var password = await TokenAsync(anonymous, new() { ["grant_type"] = "password", ["username"] = "anna", ["password"] = "x" });

        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_client"), (wrong.StatusCode, (await JsonAsync(wrong)).GetProperty("error").GetString()));
        Assert.Equal((HttpStatusCode.BadRequest, "unsupported_grant_type"), (password.StatusCode, (await JsonAsync(password)).GetProperty("error").GetString()));
    }

    [Fact]
    public async Task RevokingTheClient_EndsItsTokens_AndAnExpiredTokenNoLongerWorks()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, secret, id) = await CreateClientAsync(owner);
        using var first = WithToken(factory, await AccessTokenAsync(factory, clientId, secret));
        var (otherId, otherSecret, _) = await CreateClientAsync(owner);
        using var second = WithToken(factory, await AccessTokenAsync(factory, otherId, otherSecret));

        (await owner.DeleteAsync(Url($"/api/app/oauth-clients/{id}"), Cancel)).EnsureSuccessStatusCode();
        await SurveysApiTests.ScalarAsync(factory, $"UPDATE access_token SET expires_at = now() - interval '1 second' WHERE client_id IN (SELECT id FROM oauth_client WHERE client_id = '{otherId}')");

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync(Url("/api/v1/surveys"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync(Url("/api/v1/surveys"), Cancel)).StatusCode);
        Assert.Equal(1L, (long)(await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'oauth-client.revoked'"))!);
    }

    [Fact]
    public async Task AClientsChanges_AreAuditedWithTheClientAsActor()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, secret, id) = await CreateClientAsync(owner, ["webhooks:manage"]);
        using var caller = WithToken(factory, await AccessTokenAsync(factory, clientId, secret));

        var created = await caller.PostAsJsonAsync(Url("/api/v1/webhooks"), new { url = "https://hr.acme.at/askwerk", events = Published }, Cancel);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"OAuthClient|{id}", await SurveysApiTests.ScalarAsync(factory, "SELECT actor_kind || '|' || actor_id FROM audit_log WHERE action = 'webhook.created'"));
    }

    [Fact]
    public async Task TheApiPage_ListsClients_ForOwnersAndAdminsOnly()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, _, _) = await CreateClientAsync(owner);

        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/oauth-clients"), Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");
        var editor = await owner.GetAsync(Url("/api/app/oauth-clients"), Cancel);

        var item = list.GetProperty("items")[0];
        Assert.Equal((clientId, "HR system"), (item.GetProperty("clientId").GetString(), item.GetProperty("name").GetString()));
        Assert.False(item.TryGetProperty("clientSecret", out _));
        Assert.Equal(HttpStatusCode.Forbidden, editor.StatusCode);
    }
}
