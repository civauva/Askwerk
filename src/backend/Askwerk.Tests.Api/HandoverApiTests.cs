// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Askwerk.Tests.Integration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Askwerk.Tests.Api;

/// <summary>
/// Identity integration step 1 (AWK-12, W22): Organisation → Sign-in, the hand-over of a host product's users with a
/// signed token, and the provisioning API (scope users:manage) with members locked on the Members page.
/// </summary>
public sealed class HandoverApiTests(PostgresServer server)
{
    private const string Issuer = "https://hr.example.org";
    private static readonly string[] Provisioning = ["users:manage"];
    private static readonly string[] AdminsGroup = ["HR-Survey-Admins"];
    private static readonly string[] NoGroups = [];
    private static readonly string[] NotAKey = ["not a key"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A token as a host would sign it.</summary>
    internal static string Token(AsymmetricAlgorithm key, string audience, string subject, string email, string[] groups, DateTimeOffset? expires = null, string? jti = null, string issuer = Issuer, string? returnTo = null, string? survey = null, string? locale = null)
    {
        var es = key is ECDsa;
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = es ? "ES256" : "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["sub"] = subject,
            ["email"] = email,
            ["name"] = "Clara Weiss",
            ["groups"] = groups,
            ["exp"] = (expires ?? DateTimeOffset.UtcNow.AddMinutes(2)).ToUnixTimeSeconds(),
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
            ["return"] = returnTo,
            ["survey"] = survey,
            ["locale"] = locale,
        }));
        var signed = Encoding.ASCII.GetBytes(header + "." + payload);
        var signature = key is ECDsa ec ? ec.SignData(signed, HashAlgorithmName.SHA256) : ((RSA)key).SignData(signed, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return header + "." + payload + "." + Base64Url(signature);
    }

    /// <summary>Switches the hand-over (and provisioning) on with the key; returns the audience.</summary>
    internal static async Task<string> SetUpAsync(HttpClient owner, AsymmetricAlgorithm key, string? defaultRole = "viewer", bool provisioning = false)
    {
        var pem = key is ECDsa ec ? ec.ExportSubjectPublicKeyInfoPem() : ((RSA)key).ExportSubjectPublicKeyInfoPem();
        var saved = await owner.PutAsJsonAsync(
            Url("/api/app/sign-in"),
            new { handoverEnabled = true, issuer = Issuer, keys = new[] { pem }, jwksUrl = (string?)null, provisioningEnabled = provisioning, systemName = "HR Suite", roleRules = new[] { new { group = "HR-Survey-Admins", role = "admin" } }, defaultRole },
            Cancel);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return (await saved.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("audience").GetString()!;
    }

    private static HttpClient Browser(AskwerkApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    [Fact]
    public async Task SignInSettings_AreOffByDefault_OwnersOnly_AndKeysMustParse()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (admin, _) = await MembersApiTests.JoinedAsync(factory, owner, "ben@acme.at", "admin");
        using var _ = admin;

        var settings = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/sign-in"), Cancel);
        var bad = await owner.PutAsJsonAsync(Url("/api/app/sign-in"), new { handoverEnabled = true, issuer = Issuer, keys = NotAKey, provisioningEnabled = false, defaultRole = "viewer" }, Cancel);
        var noIssuer = await owner.PutAsJsonAsync(Url("/api/app/sign-in"), new { handoverEnabled = true, keys = Array.Empty<string>(), jwksUrl = "https://hr.example.org/jwks", provisioningEnabled = false, defaultRole = "viewer" }, Cancel);

        Assert.False(settings.GetProperty("handoverEnabled").GetBoolean());
        Assert.StartsWith("askwerk:", settings.GetProperty("audience").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url("/api/app/sign-in"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noIssuer.StatusCode);
    }

    [Fact]
    public async Task AHandover_SignsTheHostUserIn_WithTheMappedRole_Once()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = RSA.Create(2048);
        var audience = await SetUpAsync(owner, key);
        var token = Token(key, audience, "u-1001", "clara@acme.at", AdminsGroup, returnTo: "/surveys");
        using var browser = Browser(factory);

        var first = await browser.GetAsync(Url($"/handover?token={token}"), Cancel);
        var me = await browser.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        using var other = Browser(factory);
        var again = await other.PostAsync(Url("/handover"), new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }), Cancel);

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/surveys", first.Headers.Location?.OriginalString);
        Assert.Equal(("clara@acme.at", "signed-in", "admin"), (me.GetProperty("email").GetString(), me.GetProperty("state").GetString(), me.GetProperty("organisations")[0].GetProperty("role").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Contains("no longer works", await again.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'user.provisioned'"));
    }

    [Fact]
    public async Task BadTokens_AreRefused_AndNoRuleWithNotLetIn_GivesNoAccess()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var audience = await SetUpAsync(owner, key, defaultRole: null);
        using var browser = Browser(factory);

        async Task<HttpStatusCode> StatusAsync(string token) => (await browser.GetAsync(Url($"/handover?token={token}"), Cancel)).StatusCode;

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Token(stranger, audience, "u-1", "a@acme.at", AdminsGroup)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Token(key, audience, "u-1", "a@acme.at", AdminsGroup, expires: DateTimeOffset.UtcNow.AddMinutes(-5))));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Token(key, audience, "u-1", "a@acme.at", AdminsGroup, expires: DateTimeOffset.UtcNow.AddHours(1))));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Token(key, audience, "u-1", "a@acme.at", AdminsGroup, issuer: "https://other.example.org")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(Token(key, $"askwerk:{Guid.NewGuid()}", "u-1", "a@acme.at", AdminsGroup)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("not.a.token"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(Token(key, audience, "u-2", "b@acme.at", NoGroups)));
        Assert.Equal(HttpStatusCode.Redirect, await StatusAsync(Token(key, audience, "u-1", "a@acme.at", AdminsGroup)));
    }

    [Fact]
    public async Task Provisioning_CreatesChangesAndDeactivates_AndLocksMembers()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner, Provisioning);
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var off = await api.PutAsJsonAsync(Url("/api/v1/users/u-7"), new { email = "dora@acme.at", name = "Dora Berg", role = "editor" }, Cancel);
        using var key = RSA.Create(2048);
        await SetUpAsync(owner, key, provisioning: true);
        var created = await api.PutAsJsonAsync(Url("/api/v1/users/u-7"), new { email = "dora@acme.at", name = "Dora Berg", role = "editor" }, Cancel);
        var changed = await api.PutAsJsonAsync(Url("/api/v1/users/u-7"), new { email = "dora@acme.at", name = "Dora Berg-Maier", role = "analyst" }, Cancel);
        var asOwner = await api.PutAsJsonAsync(Url("/api/v1/users/u-8"), new { email = "eva@acme.at", name = "Eva", role = "owner" }, Cancel);
        var members = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/members"), Cancel);
        var dora = members.GetProperty("members").EnumerateArray().Single(member => member.GetProperty("email").GetString() == "dora@acme.at");
        var locked = await owner.PutAsJsonAsync(Url($"/api/app/members/{dora.GetProperty("id").GetString()}/role"), new { role = "viewer" }, Cancel);
        var deleted = await api.DeleteAsync(Url("/api/v1/users/u-7"), Cancel);
        var listed = await api.GetFromJsonAsync<JsonElement>(Url("/api/v1/users"), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var view = await changed.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(("analyst", "Dora Berg-Maier"), (view.GetProperty("role").GetString(), view.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, asOwner.StatusCode);
        Assert.Equal("HR Suite", dora.GetProperty("managedBy").GetString());
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var item = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("active").GetBoolean());
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'user.deactivated' AND actor_kind = 'ApiKey'"));
    }

    [Fact]
    public async Task KeysWithoutUsersManage_CannotProvision()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner);
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(Url("/api/v1/users"), Cancel)).StatusCode);
    }
}
