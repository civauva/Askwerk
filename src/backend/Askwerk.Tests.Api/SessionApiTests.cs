// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Sign-in, lockout, required two-factor for owners, recovery codes, organisations and sign-out (AWK-16, W1/W3).</summary>
public sealed class SessionApiTests(PostgresServer server)
{
    private const string Password = "correct horse battery";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string password = Password) =>
        client.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password }, Cancel);

    /// <summary>Runs setup (which signs the owner in with two-factor pending), then signs out.</summary>
    private static async Task SetUpOwnerAsync(HttpClient client)
    {
        (await client.PostAsJsonAsync(Url("/api/app/setup"), SetupApiTests.SetupBody(), Cancel)).EnsureSuccessStatusCode();
        (await client.DeleteAsync(Url("/api/app/session"), Cancel)).EnsureSuccessStatusCode();
    }

    /// <summary>Signs in and turns two-factor on; returns the authenticator key and the recovery codes.</summary>
    private static async Task<(string Key, string[] RecoveryCodes)> EnableTwoFactorAsync(HttpClient client)
    {
        await SignInAsync(client);
        var setup = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/two-factor"), Cancel);
        var key = setup.GetProperty("sharedKey").GetString()!;
        var enabled = await client.PostAsJsonAsync(Url("/api/app/account/two-factor"), new { code = Totp.Code(key, DateTimeOffset.UtcNow) }, Cancel);
        var codes = (await JsonAsync(enabled)).GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()!).ToArray();

        return (key, codes);
    }

    [Fact]
    public async Task SignIn_WrongPassword_IsRefusedWithTheTriesLeftAndNoHintWhetherTheEmailExists()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);

        var wrongPassword = await SignInAsync(client, "wrong password!!");
        var unknownEmail = await client.PostAsJsonAsync(Url("/api/app/session"), new { email = "nobody@acme.at", password = Password }, Cancel);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (wrongPassword.StatusCode, unknownEmail.StatusCode));
        var problem = await JsonAsync(wrongPassword);
        Assert.Equal("invalid-credentials", problem.GetProperty("code").GetString());
        Assert.Equal(4, problem.GetProperty("triesLeft").GetInt32());
        Assert.Equal("invalid-credentials", (await JsonAsync(unknownEmail)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SignIn_FiveWrongPasswords_LocksTheAccountForFifteenMinutes()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await SignInAsync(client, "wrong password!!");
        }

        var response = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal("locked", problem.GetProperty("code").GetString());
        var until = problem.GetProperty("lockedUntil").GetDateTimeOffset();
        Assert.InRange(until - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task SignIn_OwnerWithoutTwoFactor_MustSetItUpBeforeAnythingElse()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);

        var signIn = await SignInAsync(client);
        var switchTenant = await client.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = Guid.NewGuid() }, Cancel);

        Assert.Equal("two-factor-setup-required", (await JsonAsync(signIn)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, switchTenant.StatusCode);
    }

    [Fact]
    public async Task TwoFactorSetup_ValidCode_TurnsItOnAndGivesTenRecoveryCodes()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);

        var (key, codes) = await EnableTwoFactorAsync(client);

        Assert.Matches("^[A-Z2-7]{32}$", key);
        Assert.Equal(10, codes.Length);
        var me = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        Assert.Equal("signed-in", me.GetProperty("state").GetString());
        Assert.True(me.GetProperty("twoFactorEnabled").GetBoolean());
    }

    [Fact]
    public async Task TwoFactorSetup_ShowsTheKeyAsAnAuthenticatorLink()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        await SignInAsync(client);

        var setup = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/account/two-factor"), Cancel);

        Assert.StartsWith("otpauth://totp/Acme%20Surveys:anna%40acme.at?secret=", setup.GetProperty("authenticatorUri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoFactorSetup_WrongCode_IsRefusedAtTheCode()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        await SignInAsync(client);
        await client.GetAsync(Url("/api/app/account/two-factor"), Cancel);

        var response = await client.PostAsJsonAsync(Url("/api/app/account/two-factor"), new { code = "000000" }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("code-invalid", (await JsonAsync(response)).GetProperty("errors").GetProperty("code")[0].GetString());
    }

    [Fact]
    public async Task SignIn_WithTwoFactorOn_AsksForTheCodeAndThenSignsIn()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        var (key, _) = await EnableTwoFactorAsync(client);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);

        var password = await SignInAsync(client);
        var meBetween = await client.GetAsync(Url("/api/app/me"), Cancel);
        var code = await client.PostAsJsonAsync(Url("/api/app/session/two-factor"), new { code = Totp.Code(key, DateTimeOffset.UtcNow), rememberBrowser = false }, Cancel);

        Assert.Equal("two-factor-required", (await JsonAsync(password)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, meBetween.StatusCode);
        Assert.Equal("signed-in", (await JsonAsync(code)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task SignIn_InATrustedBrowser_SkipsTheCodeAndOpensTheOrganisation()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        var (key, _) = await EnableTwoFactorAsync(client);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);
        await SignInAsync(client);
        await client.PostAsJsonAsync(Url("/api/app/session/two-factor"), new { code = Totp.Code(key, DateTimeOffset.UtcNow), rememberBrowser = true }, Cancel);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);

        var password = await SignInAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        Assert.Equal("signed-in", (await JsonAsync(password)).GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, me.GetProperty("currentOrganisationId").ValueKind);
    }

    [Fact]
    public async Task SignIn_RecoveryCode_WorksOnce()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        var (_, codes) = await EnableTwoFactorAsync(client);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);
        await SignInAsync(client);

        var first = await client.PostAsJsonAsync(Url("/api/app/session/recovery"), new { code = codes[0] }, Cancel);
        await client.DeleteAsync(Url("/api/app/session"), Cancel);
        await SignInAsync(client);
        var second = await client.PostAsJsonAsync(Url("/api/app/session/recovery"), new { code = codes[0] }, Cancel);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Me_SignedIn_ListsTheOrganisationsWithRoleAndTheCurrentOne()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        await EnableTwoFactorAsync(client);

        var me = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        Assert.Equal(("Anna Berger", "anna@acme.at"), (me.GetProperty("name").GetString(), me.GetProperty("email").GetString()));
        var organisation = Assert.Single(me.GetProperty("organisations").EnumerateArray());
        Assert.Equal(("Acme GmbH", "acme", "owner"), (organisation.GetProperty("name").GetString(), organisation.GetProperty("slug").GetString(), organisation.GetProperty("role").GetString()));
        Assert.Equal(organisation.GetProperty("id").GetGuid(), me.GetProperty("currentOrganisationId").GetGuid());
    }

    [Fact]
    public async Task SwitchOrganisation_NotAMember_IsForbidden()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        await EnableTwoFactorAsync(client);

        var response = await client.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = Guid.NewGuid() }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        await EnableTwoFactorAsync(client);

        await client.DeleteAsync(Url("/api/app/session"), Cancel);
        var me = await client.GetAsync(Url("/api/app/me"), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task SignIn_Events_AreAudited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        await SignInAsync(client, "wrong password!!");
        await EnableTwoFactorAsync(client);

        var verify = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/audit/verify"), Cancel);

        // instance.initialized, setup.completed, user.created, sign-in failed, sign-in succeeded, two-factor enabled.
        Assert.Equal(6, verify.GetProperty("entries").GetInt32());
        Assert.True(verify.GetProperty("isIntact").GetBoolean());
    }

    [Fact]
    public async Task TwoFactor_AtRest_TheKeyIsEncryptedAndTheRecoveryCodesAreHashed()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpOwnerAsync(client);
        var (key, codes) = await EnableTwoFactorAsync(client);

        await using var connection = new Npgsql.NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new Npgsql.NpgsqlCommand("SELECT authenticator_key, recovery_codes FROM app_user", connection);
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        await reader.ReadAsync(Cancel);
        var storedKey = reader.GetString(0);
        var storedCodes = reader.GetString(1);

        Assert.DoesNotContain(key, storedKey, StringComparison.Ordinal);
        Assert.All(codes, code => Assert.DoesNotContain(code, storedCodes, StringComparison.Ordinal));
    }
}
