// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// The published view (AWK-7, W12 B): a link on every verified domain, the primary one first, and embedding only on
/// websites the organisation allows (Content-Security-Policy frame-ancestors on survey pages).
/// </summary>
public sealed class PublishingApiTests(PostgresServer server)
{
    private static readonly string[] AcmeWebsites = ["https://www.acme.at", "https://intranet.acme.at:8443/"];
    private static readonly string[] HostOnly = ["www.acme.at"];
    private static readonly string[] Wildcard = ["https://*"];
    private static readonly string[] Website = ["https://www.acme.at"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    /// <summary>umfragen.acme.at verified and primary, umfrage2.acme.at still waiting.</summary>
    private static async Task WithDomainsAsync(AskwerkApiFactory factory, HttpClient owner)
    {
        foreach (var hostname in new[] { "umfragen.acme.at", "umfrage2.acme.at" })
        {
            var domain = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/domains"), new { hostname }, Cancel));

            if (hostname == "umfragen.acme.at")
            {
                factory.Dns.Cnames[hostname] = "surveys.acme.at";
                factory.Dns.Txts[domain.GetProperty("txtName").GetString()!] = [domain.GetProperty("txtValue").GetString()!];
                await owner.PostAsync(Url($"/api/app/domains/{domain.GetProperty("id").GetString()}/verify"), null, Cancel);
                await owner.PutAsJsonAsync(Url("/api/app/domains/primary"), new { domainId = domain.GetProperty("id").GetString() }, Cancel);
            }
        }
    }

    private static async Task<string> PublishedAsync(HttpClient owner)
    {
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return id;
    }

    private static (string, string, bool, string)[] Links(JsonElement body) =>
        [.. body.GetProperty("links").EnumerateArray().Select(link => (
            link.GetProperty("kind").GetString()!,
            link.GetProperty("hostname").GetString()!,
            link.GetProperty("isPrimary").GetBoolean(),
            link.GetProperty("url").GetString()!))];

    [Fact]
    public async Task Links_OnEveryVerifiedDomain_PrimaryFirst()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await WithDomainsAsync(factory, owner);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();

        var published = await JsonAsync(await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel));
        var later = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/links"), Cancel);

        var code = (string)(await SurveysApiTests.ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{id}'"))!;
        (string, string, bool, string)[] expected =
        [
            ("domain", "umfragen.acme.at", true, $"https://umfragen.acme.at/s/{code}"),
            ("system", "surveys.acme.at", false, $"https://surveys.acme.at/s/{code}"),
        ];
        Assert.Equal(expected, Links(published));
        Assert.Equal(expected, Links(later));
        Assert.Equal(code, later.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Embedding_IsOff_UntilTheOrganisationListsWebsites()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await PublishedAsync(owner);
        var code = (string)(await SurveysApiTests.ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{id}'"))!;
        using var visitor = await factory.CreateReadyClientAsync();

        var before = await visitor.GetAsync(Url($"/s/{code}"), Cancel);
        var saved = await owner.PutAsJsonAsync(Url("/api/app/embedding"), new { origins = AcmeWebsites }, Cancel);
        var embedding = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/embedding"), Cancel);
        var after = await visitor.GetAsync(Url($"/s/{code}"), Cancel);
        var byId = await visitor.GetAsync(Url($"/s/{id}"), Cancel);
        var unknown = await visitor.GetAsync(Url("/s/zzzzzzzz"), Cancel);

        Assert.Equal("frame-ancestors 'self'", before.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(["https://www.acme.at", "https://intranet.acme.at:8443"], embedding.GetProperty("origins").EnumerateArray().Select(origin => origin.GetString()));
        Assert.Equal("frame-ancestors 'self' https://www.acme.at https://intranet.acme.at:8443", after.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("frame-ancestors 'self' https://www.acme.at https://intranet.acme.at:8443", byId.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("frame-ancestors 'self'", unknown.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'embedding.changed'"));
    }

    [Fact]
    public async Task Embedding_OnlyWebsiteOrigins_AndOnlyOwnersAndAdmins()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var invalid = await owner.PutAsJsonAsync(Url("/api/app/embedding"), new { origins = HostOnly }, Cancel);
        var wildcard = await owner.PutAsJsonAsync(Url("/api/app/embedding"), new { origins = Wildcard }, Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");
        var editor = await owner.PutAsJsonAsync(Url("/api/app/embedding"), new { origins = Website }, Cancel);

        Assert.Equal("origin-invalid", (await JsonAsync(invalid)).GetProperty("errors").GetProperty("origins")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, wildcard.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, editor.StatusCode);
    }
}
