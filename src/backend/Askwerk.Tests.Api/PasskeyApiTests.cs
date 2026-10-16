// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Passkeys (AWK-16, W4): add, list, rename, remove, and sign in; a passkey counts as two-factor for owners and admins.</summary>
public sealed class PasskeyApiTests(PostgresServer server)
{
    private const string Password = "correct horse battery";
    private const string Origin = "http://localhost";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<HttpClient> OwnerAsync(AskwerkApiFactory factory, bool signOut = false)
    {
        var client = await factory.CreateReadyClientAsync();
        client.DefaultRequestHeaders.Add("Origin", Origin);
        (await client.PostAsJsonAsync(Url("/api/app/setup"), SetupApiTests.SetupBody(), Cancel)).EnsureSuccessStatusCode();

        if (signOut)
        {
            await client.DeleteAsync(Url("/api/app/session"), Cancel);
        }

        return client;
    }

    private static async Task<HttpResponseMessage> AddPasskeyAsync(HttpClient client, TestAuthenticator authenticator, string name = "Pixel 7")
    {
        var options = await client.PostAsync(Url("/api/app/account/passkeys/options"), null, Cancel);
        var credential = authenticator.Create(await options.Content.ReadAsStringAsync(Cancel));

        return await client.PostAsJsonAsync(Url("/api/app/account/passkeys"), new { credential, name }, Cancel);
    }

    private static async Task<HttpResponseMessage> SignInWithPasskeyAsync(HttpClient client, TestAuthenticator authenticator, bool tamper = false)
    {
        var options = await client.PostAsync(Url("/api/app/session/passkey/options"), null, Cancel);
        var credential = authenticator.Get(await options.Content.ReadAsStringAsync(Cancel), tamper);

        return await client.PostAsJsonAsync(Url("/api/app/session/passkey"), new { credential }, Cancel);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    [Fact]
    public async Task AddPasskey_OwnerRightAfterSetup_CountsAsTwoFactorAndLiftsThePendingStep()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);

        var added = await AddPasskeyAsync(client, authenticator);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal("signed-in", (await JsonAsync(added)).GetProperty("state").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        Assert.Equal("signed-in", me.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Passkeys_AfterAdding_AreListedWithNameAndDates()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator, "MacBook · Touch ID");

        var passkeys = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/passkeys"), Cancel);

        var passkey = Assert.Single(passkeys.EnumerateArray());
        Assert.Equal(authenticator.CredentialId, passkey.GetProperty("id").GetString());
        Assert.Equal("MacBook · Touch ID", passkey.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, passkey.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, passkey.GetProperty("lastUsedAt").ValueKind);
    }

    [Fact]
    public async Task SignInWithPasskey_SignsTheOwnerInWithoutPasswordOrAppCode()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);

        var signIn = await SignInWithPasskeyAsync(client, authenticator);

        Assert.Equal("signed-in", (await JsonAsync(signIn)).GetProperty("state").GetString());
        var passkeys = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/passkeys"), Cancel);
        Assert.Equal(JsonValueKind.String, passkeys[0].GetProperty("lastUsedAt").ValueKind);
    }

    [Fact]
    public async Task SignInWithPasskey_BadSignature_IsRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);

        var signIn = await SignInWithPasskeyAsync(client, authenticator, tamper: true);

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.Equal("passkey-invalid", (await JsonAsync(signIn)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SignInWithPassword_OwnerWithPasskeyButNoApp_IsAskedToUseThePasskey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);

        var signIn = await client.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password = Password }, Cancel);
        var me = await client.GetAsync(Url("/api/app/me"), Cancel);

        Assert.Equal("passkey-required", (await JsonAsync(signIn)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task RenamePasskey_ChangesItsName()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator);

        var rename = await client.PutAsJsonAsync(Url($"/api/app/account/passkeys/{authenticator.CredentialId}"), new { name = "Work phone" }, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
        var passkeys = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/passkeys"), Cancel);
        Assert.Equal("Work phone", passkeys[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task RemovePasskey_NeedsThePasswordFirst()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator);
        var path = Url($"/api/app/account/passkeys/{authenticator.CredentialId}/remove");

        var wrong = await client.PostAsJsonAsync(path, new { password = "not my password" }, Cancel);
        var right = await client.PostAsJsonAsync(path, new { password = Password }, Cancel);

        Assert.Equal("password-wrong", (await JsonAsync(wrong)).GetProperty("errors").GetProperty("password")[0].GetString());
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        var passkeys = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/passkeys"), Cancel);
        Assert.Empty(passkeys.EnumerateArray());
    }

    [Fact]
    public async Task AddPasskey_NotSignedIn_IsUnauthorized()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory, signOut: true);

        var options = await client.PostAsync(Url("/api/app/account/passkeys/options"), null, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, options.StatusCode);
    }

    [Fact]
    public async Task PasskeyEvents_AreAudited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await OwnerAsync(factory);
        using var authenticator = new TestAuthenticator(Origin);
        await AddPasskeyAsync(client, authenticator);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);
        await SignInWithPasskeyAsync(client, authenticator);

        var verify = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/audit/verify"), Cancel);

        // instance.initialized, setup.completed, user.created, passkey added, signed in with passkey.
        Assert.Equal(5, verify.GetProperty("entries").GetInt32());
    }
}
