// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Settings → API (AWK-25, W14 C and D): owners and admins create keys for the Public API, see each key once, and
/// revoke them. Every change is audited.
/// </summary>
public sealed class ApiKeysApiTests(PostgresServer server)
{
    private static readonly string[] SurveysRead = ["surveys:read"];
    private static readonly string[] SettingsWrite = ["settings:write"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    /// <summary>Creates a key as the signed-in owner and returns its secret and id.</summary>
    internal static async Task<(string Secret, string Id)> CreateKeyAsync(HttpClient owner, string[]? scopes = null, int? expiresInDays = 90)
    {
        var created = await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "Reporting server", scopes = scopes ?? ["surveys:read", "responses:read"], expiresInDays }, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);

        return (body.GetProperty("secret").GetString()!, body.GetProperty("key").GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Create_ShowsTheSecretOnce_ThenOnlyItsBeginning()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var (secret, id) = await CreateKeyAsync(owner);
        var listed = await owner.GetStringAsync(Url("/api/app/api-keys"), Cancel);
        var key = JsonDocument.Parse(listed).RootElement.GetProperty("items")[0];

        Assert.StartsWith("awk_", secret, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, listed, StringComparison.Ordinal);
        Assert.Equal((id, "Reporting server", secret[..8] + "…"), (key.GetProperty("id").GetString(), key.GetProperty("name").GetString(), key.GetProperty("shown").GetString()));
        Assert.Equal(["surveys:read", "responses:read"], key.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Equal(key.GetProperty("createdAt").GetDateTimeOffset().AddDays(90), key.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, key.GetProperty("lastUsedAt").ValueKind);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'api-key.created'"));
    }

    [Fact]
    public async Task Create_NeverExpiring_AndRefusesBadInput()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var never = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "HR sync", scopes = SurveysRead, expiresInDays = (int?)null }, Cancel));
        var nameless = await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = " ", scopes = SurveysRead, expiresInDays = 90 }, Cancel);
        var settings = await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "Admin", scopes = SettingsWrite, expiresInDays = 90 }, Cancel);
        var oddExpiry = await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "Odd", scopes = SurveysRead, expiresInDays = 7 }, Cancel);

        Assert.Equal(JsonValueKind.Null, never.GetProperty("key").GetProperty("expiresAt").ValueKind);
        Assert.Equal("name-required", (await JsonAsync(nameless)).GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Equal("unknown-scope", (await JsonAsync(settings)).GetProperty("errors").GetProperty("scopes")[0].GetString());
        Assert.Equal("expiry-not-offered", (await JsonAsync(oddExpiry)).GetProperty("errors").GetProperty("expiresInDays")[0].GetString());
    }

    [Fact]
    public async Task Revoke_RemovesTheKeyFromTheList_AndIsAudited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (_, id) = await CreateKeyAsync(owner);

        var revoked = await owner.DeleteAsync(Url($"/api/app/api-keys/{id}"), Cancel);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/api-keys"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.NotNull(await SurveysApiTests.ScalarAsync(factory, $"SELECT revoked_at FROM api_key WHERE id = '{id}'"));
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'api-key.revoked'"));
    }

    [Fact]
    public async Task Editors_DoNotManageKeys()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");

        var list = await owner.GetAsync(Url("/api/app/api-keys"), Cancel);
        var created = await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "Mine", scopes = SurveysRead, expiresInDays = 90 }, Cancel);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (list.StatusCode, created.StatusCode));
    }

    [Fact]
    public async Task Keys_AreForTheOrganisationOnly()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (_, id) = await CreateKeyAsync(owner);
        var other = Guid.CreateVersion7();
        await SurveysApiTests.ScalarAsync(factory, $"""
            INSERT INTO tenant (id, name, slug, created_at) VALUES ('{other}', 'Acme Schweiz AG', 'acme-ch', now());
            INSERT INTO membership (id, tenant_id, user_id, role, created_at) SELECT gen_random_uuid(), '{other}', id, 'Owner', now() FROM app_user;
            """);

        (await owner.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = other }, Cancel)).EnsureSuccessStatusCode();
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/api-keys"), Cancel);
        var revoke = await owner.DeleteAsync(Url($"/api/app/api-keys/{id}"), Cancel);

        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }
}
