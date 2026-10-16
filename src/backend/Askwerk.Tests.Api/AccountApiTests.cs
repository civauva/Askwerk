// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>The Account and security page on the server (AWK-16, W4): password, two-factor, preferences, sign out everywhere.</summary>
public sealed class AccountApiTests(PostgresServer server)
{
    private const string Password = "correct horse battery";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    /// <summary>Setup, then two-factor on; returns the authenticator key and the recovery codes.</summary>
    internal static async Task<(string Key, string[] Codes)> OwnerWithTwoFactorAsync(HttpClient client)
    {
        (await client.PostAsJsonAsync(Url("/api/app/setup"), SetupApiTests.SetupBody(), Cancel)).EnsureSuccessStatusCode();
        var setup = await client.GetAsync(Url("/api/app/account/two-factor"), Cancel);
        Assert.True(setup.IsSuccessStatusCode, $"{setup.StatusCode}: {await setup.Content.ReadAsStringAsync(Cancel)}");
        var key = (await JsonAsync(setup)).GetProperty("sharedKey").GetString()!;
        var enabled = await client.PostAsJsonAsync(Url("/api/app/account/two-factor"), new { code = Totp.Code(key, DateTimeOffset.UtcNow) }, Cancel);
        Assert.True(enabled.IsSuccessStatusCode, $"{enabled.StatusCode}: {await enabled.Content.ReadAsStringAsync(Cancel)}");
        var codes = (await JsonAsync(enabled)).GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()!).ToArray();

        return (key, codes);
    }

    private static async Task SignInAsync(HttpClient client, string key, string password = Password)
    {
        await client.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password }, Cancel);
        await client.PostAsJsonAsync(Url("/api/app/session/two-factor"), new { code = Totp.Code(key, DateTimeOffset.UtcNow), rememberBrowser = false }, Cancel);
    }

    [Fact]
    public async Task ChangePassword_WithTheCurrentOne_ReplacesItAndSignsOutOtherSessions()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        using var otherBrowser = await factory.CreateReadyClientAsync();
        var (key, _) = await OwnerWithTwoFactorAsync(client);
        await SignInAsync(otherBrowser, key);

        var change = await client.PostAsJsonAsync(Url("/api/app/account/password"), new { currentPassword = Password, newPassword = "a brand new passphrase" }, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url("/api/app/me"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync(Url("/api/app/me"), Cancel)).StatusCode);
        var oldPassword = await otherBrowser.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password = Password }, Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    [Theory]
    [InlineData("wrong current one", "a brand new passphrase", "currentPassword", "password-wrong")]
    [InlineData(Password, "short", "newPassword", "password-too-short")]
    public async Task ChangePassword_Invalid_IsReportedAtTheField(string current, string next, string field, string code)
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await OwnerWithTwoFactorAsync(client);

        var change = await client.PostAsJsonAsync(Url("/api/app/account/password"), new { currentPassword = current, newPassword = next }, Cancel);

        Assert.Equal(code, (await JsonAsync(change)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task NewRecoveryCodes_AfterThePassword_ReplaceTheOldOnes()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        var (_, oldCodes) = await OwnerWithTwoFactorAsync(client);

        var renewed = await client.PostAsJsonAsync(Url("/api/app/account/two-factor/recovery-codes"), new { password = Password }, Cancel);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);
        await client.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password = Password }, Cancel);
        var oldCode = await client.PostAsJsonAsync(Url("/api/app/session/recovery"), new { code = oldCodes[0] }, Cancel);

        Assert.Equal(10, (await JsonAsync(renewed)).GetProperty("recoveryCodes").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, oldCode.StatusCode);
    }

    [Fact]
    public async Task MoveToNewPhone_ResetsTheKeyAndAsksForTheSetupAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        var (oldKey, _) = await OwnerWithTwoFactorAsync(client);

        var reset = await client.PostAsJsonAsync(Url("/api/app/account/two-factor/reset"), new { password = Password }, Cancel);

        Assert.Equal("two-factor-setup-required", (await JsonAsync(reset)).GetProperty("state").GetString());
        var newKey = (await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/two-factor"), Cancel)).GetProperty("sharedKey").GetString();
        Assert.NotEqual(oldKey, newKey);
    }

    [Fact]
    public async Task TurnOffTwoFactor_Owner_IsRefusedBecauseTheRoleRequiresIt()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await OwnerWithTwoFactorAsync(client);

        var off = await client.PostAsJsonAsync(Url("/api/app/account/two-factor/disable"), new { password = Password }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.Equal("two-factor-required-for-role", (await JsonAsync(off)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_ShowsTwoFactorDetailsForThePage()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await OwnerWithTwoFactorAsync(client);

        var me = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        Assert.Equal(10, me.GetProperty("recoveryCodesLeft").GetInt32());
        Assert.True(me.GetProperty("twoFactorRequired").GetBoolean());
    }

    [Fact]
    public async Task Preferences_AreSavedWithTheAccount()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await OwnerWithTwoFactorAsync(client);

        var save = await client.PutAsJsonAsync(Url("/api/app/account/preferences"), new { language = "de", theme = "dark" }, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        Assert.Equal(("de", "dark"), (me.GetProperty("language").GetString(), me.GetProperty("theme").GetString()));
    }

    [Fact]
    public async Task Preferences_UnknownValues_AreRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await OwnerWithTwoFactorAsync(client);

        var save = await client.PutAsJsonAsync(Url("/api/app/account/preferences"), new { language = "fr", theme = "pink" }, Cancel);

        var errors = (await JsonAsync(save)).GetProperty("errors");
        Assert.Equal(("language-unknown", "theme-unknown"), (errors.GetProperty("language")[0].GetString(), errors.GetProperty("theme")[0].GetString()));
    }

    [Fact]
    public async Task SignedInRequests_DoNotRewriteTheSessionCookie()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await OwnerWithTwoFactorAsync(client);

        var first = await client.GetAsync(Url("/api/app/me"), Cancel);
        var second = await client.GetAsync(Url("/api/app/surveys"), Cancel);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.False(first.Headers.Contains("Set-Cookie"));
        Assert.False(second.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SignOutEverywhere_EndsOtherSessionsAndKeepsThisOne()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        using var otherBrowser = await factory.CreateReadyClientAsync();
        var (key, _) = await OwnerWithTwoFactorAsync(client);
        await SignInAsync(otherBrowser, key);

        var signOut = await client.PostAsync(Url("/api/app/account/sign-out-everywhere"), null, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url("/api/app/me"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync(Url("/api/app/me"), Cancel)).StatusCode);
    }
}
