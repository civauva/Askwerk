// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Email settings from setup step 4, the test email, password reset by link, and the lockout notice (AWK-16, W1/W2).</summary>
public sealed partial class EmailAndResetApiTests(PostgresServer server)
{
    private const string Password = "correct horse battery";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static readonly object Smtp = new { host = "smtp.acme.at", port = 587, userName = "surveys@acme.at", password = "smtp-secret", sender = "Acme Surveys <surveys@acme.at>" };

    private static object SetupWithEmail() => new
    {
        owner = new { name = "Anna Berger", email = "anna@acme.at", password = Password },
        instance = new { name = "Acme Surveys", baseAddress = "https://surveys.acme.at", defaultLanguage = "en", timeZone = "Europe/Vienna" },
        organisation = new { name = "Acme GmbH", slug = "acme" },
        email = Smtp,
    };

    private static async Task SetUpAsync(HttpClient client, object body)
    {
        (await client.PostAsJsonAsync(Url("/api/app/setup"), body, Cancel)).EnsureSuccessStatusCode();
        (await client.DeleteAsync(Url("/api/app/session"), Cancel)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SetupTestEmail_SendsThroughTheGivenServerToTheGivenAddress()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();

        var response = await client.PostAsJsonAsync(Url("/api/app/setup/test-email"), new { smtp = Smtp, to = "anna@acme.at", language = "de" }, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var (smtp, message) = Assert.Single(factory.Emails.Sent);
        Assert.Equal(("smtp.acme.at", "anna@acme.at"), (smtp.Host, message.To));
        Assert.Equal("Askwerk: Test-E-Mail", message.Subject);
    }

    [Fact]
    public async Task PasswordReset_KnownEmail_SendsALinkThatSetsANewPasswordOnce()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpAsync(client, SetupWithEmail());

        var request = await client.PostAsJsonAsync(Url("/api/app/password-reset"), new { email = "anna@acme.at" }, Cancel);
        var (smtp, message) = Assert.Single(factory.Emails.Sent);
        var link = ResetLink().Match(message.Body);
        var token = Uri.UnescapeDataString(link.Groups["token"].Value);
        var reset = await client.PostAsJsonAsync(Url("/api/app/password-reset/confirm"), new { email = "anna@acme.at", token, password = "a brand new passphrase" }, Cancel);
        var again = await client.PostAsJsonAsync(Url("/api/app/password-reset/confirm"), new { email = "anna@acme.at", token, password = "yet another passphrase" }, Cancel);
        var signIn = await client.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password = "a brand new passphrase" }, Cancel);

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal(("smtp.acme.at", "anna@acme.at", "Askwerk: Reset your password"), (smtp.Host, message.To, message.Subject));
        Assert.True(link.Success);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal("token-invalid", (await again.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors").GetProperty("token")[0].GetString());
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_UnknownEmail_AnswersTheSameAndSendsNothing()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpAsync(client, SetupWithEmail());

        var response = await client.PostAsJsonAsync(Url("/api/app/password-reset"), new { email = "nobody@acme.at" }, Cancel);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.Sent);
    }

    [Fact]
    public async Task PasswordReset_WithoutEmailSettings_IsUnavailable()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpAsync(client, SetupApiTests.SetupBody());

        var state = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/password-reset"), Cancel);
        var response = await client.PostAsJsonAsync(Url("/api/app/password-reset"), new { email = "anna@acme.at" }, Cancel);

        Assert.False(state.GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.Sent);
    }

    [Fact]
    public async Task Lockout_TellsTheAccountOwnerByEmail()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpAsync(client, SetupWithEmail());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync(Url("/api/app/session"), new { email = "anna@acme.at", password = "wrong password!!" }, Cancel);
        }

        var (_, message) = Assert.Single(factory.Emails.Sent);
        Assert.Equal(("anna@acme.at", "Askwerk: Sign-in paused"), (message.To, message.Subject));
    }

    [Fact]
    public async Task EmailPassword_AtRest_IsEncrypted()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await SetUpAsync(client, SetupWithEmail());

        await using var connection = new Npgsql.NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new Npgsql.NpgsqlCommand("SELECT smtp_password FROM instance_settings", connection);
        var stored = (string)(await command.ExecuteScalarAsync(Cancel))!;

        Assert.DoesNotContain("smtp-secret", stored, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"https://surveys\.acme\.at/reset-password\?email=anna%40acme\.at&token=(?<token>\S+)")]
    private static partial Regex ResetLink();
}
