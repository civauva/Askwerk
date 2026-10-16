// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>The first-run setup over HTTP (AWK-16, W2): open only until it has run once.</summary>
public sealed class SetupApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static object SetupBody(string slug = "acme", string password = "correct horse battery") => new
    {
        owner = new { name = "Anna Berger", email = "anna@acme.at", password },
        instance = new { name = "Acme Surveys", baseAddress = "https://surveys.acme.at", defaultLanguage = "de", timeZone = "Europe/Vienna" },
        organisation = new { name = "Acme GmbH", slug },
    };

    [Fact]
    public async Task GetSetup_FreshInstance_SaysSetupIsRequired()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();

        var setup = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/app/setup", UriKind.Relative), Cancel);

        Assert.True(setup.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task PostSetup_ValidInput_CreatesTheOwnerAndSignsInForTwoFactorSetup()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/app/setup", UriKind.Relative), SetupBody(), Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("two-factor-setup-required", session.GetProperty("state").GetString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("askwerk.session=", StringComparison.Ordinal) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Setup_AfterItRan_AnswersNotFound()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        await client.PostAsJsonAsync(new Uri("/api/app/setup", UriKind.Relative), SetupBody(), Cancel);

        var get = await client.GetAsync(new Uri("/api/app/setup", UriKind.Relative), Cancel);
        var post = await client.PostAsJsonAsync(new Uri("/api/app/setup", UriKind.Relative), SetupBody("other"), Cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (get.StatusCode, post.StatusCode));
    }

    [Theory]
    [InlineData("Acme!", "correct horse battery", "slug", "slug-invalid")]
    [InlineData("acme", "short", "password", "password-too-short")]
    public async Task PostSetup_InvalidInput_ReportsTheFieldAndCode(string slug, string password, string field, string code)
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/app/setup", UriKind.Relative), SetupBody(slug, password), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(code, problem.GetProperty("errors").GetProperty(field)[0].GetString());
        Assert.Equal(JsonValueKind.String, problem.GetProperty("traceId").ValueKind);
    }

    [Fact]
    public async Task SlugCheck_DuringSetup_SaysWhetherTheSlugIsFreeAndSuggestsOne()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();

        var check = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/app/setup/slug?name=Acme%20GmbH&slug=acme", UriKind.Relative), Cancel);

        Assert.True(check.GetProperty("available").GetBoolean());
        Assert.Equal("acme-gmbh", check.GetProperty("suggestion").GetString());
    }

    [Fact]
    public async Task Instance_AfterSetup_ShowsItsNameToEveryone()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();
        var before = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/app/instance", UriKind.Relative), Cancel);
        await client.PostAsJsonAsync(new Uri("/api/app/setup", UriKind.Relative), SetupBody(), Cancel);
        await client.DeleteAsync(new Uri("/api/app/session", UriKind.Relative), Cancel);

        var after = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/app/instance", UriKind.Relative), Cancel);

        Assert.Equal(JsonValueKind.Null, before.GetProperty("name").ValueKind);
        Assert.Equal("Acme Surveys", after.GetProperty("name").GetString());
    }
}
