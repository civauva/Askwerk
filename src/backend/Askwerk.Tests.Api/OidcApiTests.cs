// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Askwerk.Tests.Api;

/// <summary>
/// Identity integration step 2 (AWK-12, W22): single sign-on with an organisation's OpenID Connect provider,
/// authorization code flow with PKCE, started from /sso/{organisation}.
/// </summary>
public sealed class OidcApiTests(PostgresServer server)
{
    private static readonly string[] Admins = ["HR-Survey-Admins"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> SetUpAsync(HttpClient owner, string? defaultRole = "viewer")
    {
        var saved = await owner.PutAsJsonAsync(
            Url("/api/app/sign-in"),
            new
            {
                handoverEnabled = false,
                provisioningEnabled = false,
                roleRules = new[] { new { group = "HR-Survey-Admins", role = "admin" } },
                defaultRole,
                oidcEnabled = true,
                oidcAuthority = FakeOidcProvider.Issuer,
                oidcClientId = "askwerk",
                oidcClientSecret = "s3cret",
                oidcDisplayName = "Acme",
            },
            Cancel);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return await saved.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    /// <summary>The browser's round trip: Askwerk → provider (the test signs in there) → Askwerk's callback.</summary>
    private static async Task<HttpResponseMessage> SignInAsync(AskwerkApiFactory factory, HttpClient browser, string ssoPath, Dictionary<string, object> user)
    {
        var start = await browser.GetAsync(Url(ssoPath + "?return=/surveys"), Cancel);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var authorize = start.Headers.Location!;
        Assert.StartsWith(FakeOidcProvider.Issuer + "/authorize", authorize.ToString(), StringComparison.Ordinal);
        var query = authorize.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        Assert.Equal("S256", query["code_challenge_method"]);
        factory.Oidc.Nonce = query["nonce"];
        factory.Oidc.NextUser = user;
        var callback = new Uri(query["redirect_uri"]).AbsolutePath;

        return await browser.PostAsync(Url(callback), new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "code-1", ["state"] = query["state"] }), Cancel);
    }

    [Fact]
    public async Task SingleSignOn_SignsInWithTheMappedRole_AndShowsTheLinkToShare()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var settings = await SetUpAsync(owner);
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

        var done = await SignInAsync(factory, browser, settings.GetProperty("ssoPath").GetString()!, new() { ["sub"] = "entra-42", ["email"] = "clara@acme.at", ["name"] = "Clara Weiss", ["groups"] = Admins });
        var me = await browser.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        Assert.True(settings.GetProperty("oidcClientSecretSet").GetBoolean());
        Assert.StartsWith("/sso/callback/", settings.GetProperty("oidcCallbackPath").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Equal("/surveys", done.Headers.Location?.OriginalString);
        Assert.Equal(("clara@acme.at", "signed-in", "admin"), (me.GetProperty("email").GetString(), me.GetProperty("state").GetString(), me.GetProperty("organisations")[0].GetProperty("role").GetString()));
        var token = Assert.Single(factory.Oidc.TokenRequests);
        Assert.Equal(("authorization_code", "s3cret"), (token["grant_type"], token["client_secret"]));
        Assert.True(token.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task NoMatchingRule_WithNotLetIn_GivesNoAccess_AndAnUnknownOrganisationHasNoSso()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var settings = await SetUpAsync(owner, defaultRole: null);
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

        var refused = await SignInAsync(factory, browser, settings.GetProperty("ssoPath").GetString()!, new() { ["sub"] = "entra-7", ["email"] = "max@acme.at" });
        var unknown = await browser.GetAsync(Url("/sso/nobody-here"), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("no access", await refused.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(Url("/api/app/me"), Cancel)).StatusCode);
    }
}
