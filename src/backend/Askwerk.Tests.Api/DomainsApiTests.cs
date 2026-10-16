// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Api.App.Domains;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// Settings → Domains (AWK-7, W12 A): the system address, custom domains verified by DNS (a CNAME to the system address
/// and a TXT record with a token), one primary domain, and Caddy's ask endpoint for certificates.
/// </summary>
public sealed class DomainsApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static async Task<JsonElement> AddAsync(HttpClient owner, string hostname) =>
        await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/domains"), new { hostname }, Cancel));

    private static void SetRecords(AskwerkApiFactory factory, JsonElement domain, bool cname = true, bool txt = true)
    {
        var hostname = domain.GetProperty("hostname").GetString()!;

        if (cname)
        {
            factory.Dns.Cnames[hostname] = "surveys.acme.at.";
        }

        if (txt)
        {
            factory.Dns.Txts[domain.GetProperty("txtName").GetString()!] = ["v=spf1 -all", domain.GetProperty("txtValue").GetString()!];
        }
    }

    [Fact]
    public async Task List_TheSystemAddressIsPrimary_UntilAnotherIs()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var domains = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/domains"), Cancel);

        Assert.Equal(("surveys.acme.at", true), (domains.GetProperty("system").GetProperty("hostname").GetString(), domains.GetProperty("system").GetProperty("isPrimary").GetBoolean()));
        Assert.Equal(0, domains.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Add_ShowsTheTwoRecordsToSet()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var added = await owner.PostAsJsonAsync(Url("/api/app/domains"), new { hostname = " Umfragen.ACME.at. " }, Cancel);
        var domain = await JsonAsync(added);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(("umfragen.acme.at", "pending", "surveys.acme.at", "_askwerk.umfragen.acme.at"), (
            domain.GetProperty("hostname").GetString(),
            domain.GetProperty("status").GetString(),
            domain.GetProperty("cnameTarget").GetString(),
            domain.GetProperty("txtName").GetString()));
        Assert.Matches("^askwerk-verify=[a-z0-9]{32}$", domain.GetProperty("txtValue").GetString());
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'domain.added'"));
    }

    [Theory]
    [InlineData("https://umfragen.acme.at/s", "hostname-invalid")]
    [InlineData("umfragen acme.at", "hostname-invalid")]
    [InlineData("acme.at", "hostname-not-subdomain")]
    [InlineData("surveys.acme.at", "hostname-is-system")]
    public async Task Add_RefusesWhatCannotWork(string hostname, string code)
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var added = await owner.PostAsJsonAsync(Url("/api/app/domains"), new { hostname }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        Assert.Equal(code, (await JsonAsync(added)).GetProperty("errors").GetProperty("hostname")[0].GetString());
    }

    [Fact]
    public async Task Add_TheSameDomainTwice_IsTaken()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await AddAsync(owner, "umfragen.acme.at");

        var again = await owner.PostAsJsonAsync(Url("/api/app/domains"), new { hostname = "umfragen.acme.at" }, Cancel);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("domain-taken", (await JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Verify_SaysWhichRecordIsMissing_ThenVerifies()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var domain = await AddAsync(owner, "umfragen.acme.at");
        var id = domain.GetProperty("id").GetString();
        SetRecords(factory, domain, txt: false);

        var halfway = await JsonAsync(await owner.PostAsync(Url($"/api/app/domains/{id}/verify"), null, Cancel));
        SetRecords(factory, domain);
        var verified = await JsonAsync(await owner.PostAsync(Url($"/api/app/domains/{id}/verify"), null, Cancel));

        Assert.Equal(("pending", true, false), (halfway.GetProperty("status").GetString(), halfway.GetProperty("cnameFound").GetBoolean(), halfway.GetProperty("txtFound").GetBoolean()));
        Assert.Equal("verified", verified.GetProperty("status").GetString());
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'domain.verified'"));
    }

    [Fact]
    public async Task Primary_OnlyAVerifiedDomain_AndRemovingItMakesTheSystemAddressPrimaryAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var domain = await AddAsync(owner, "umfragen.acme.at");
        var id = domain.GetProperty("id").GetString();

        var tooEarly = await owner.PutAsJsonAsync(Url("/api/app/domains/primary"), new { domainId = id }, Cancel);
        SetRecords(factory, domain);
        await owner.PostAsync(Url($"/api/app/domains/{id}/verify"), null, Cancel);
        var made = await owner.PutAsJsonAsync(Url("/api/app/domains/primary"), new { domainId = id }, Cancel);
        var withPrimary = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/domains"), Cancel);
        await owner.DeleteAsync(Url($"/api/app/domains/{id}"), Cancel);
        var afterwards = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/domains"), Cancel);

        Assert.Equal(("domain-not-verified", HttpStatusCode.NoContent), ((await JsonAsync(tooEarly)).GetProperty("code").GetString(), made.StatusCode));
        Assert.Equal((false, true), (withPrimary.GetProperty("system").GetProperty("isPrimary").GetBoolean(), withPrimary.GetProperty("items")[0].GetProperty("isPrimary").GetBoolean()));
        Assert.Equal((true, 0), (afterwards.GetProperty("system").GetProperty("isPrimary").GetBoolean(), afterwards.GetProperty("items").GetArrayLength()));
    }

    [Fact]
    public async Task CaddyAsk_CertificatesOnlyForTheSystemAddressAndVerifiedDomains()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var domain = await AddAsync(owner, "umfragen.acme.at");
        using var caddy = await factory.CreateReadyClientAsync();

        var before = await caddy.GetAsync(Url("/api/caddy/ask?domain=umfragen.acme.at"), Cancel);
        SetRecords(factory, domain);
        await owner.PostAsync(Url($"/api/app/domains/{domain.GetProperty("id").GetString()}/verify"), null, Cancel);
        var after = await caddy.GetAsync(Url("/api/caddy/ask?domain=umfragen.acme.at"), Cancel);
        var system = await caddy.GetAsync(Url("/api/caddy/ask?domain=surveys.acme.at"), Cancel);
        var other = await caddy.GetAsync(Url("/api/caddy/ask?domain=evil.example.com"), Cancel);

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound),
            (before.StatusCode, after.StatusCode, system.StatusCode, other.StatusCode));
    }

    [Fact]
    public async Task Checks_RunByThemselves_AndGiveUpAfterSevenDays()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var soon = await AddAsync(owner, "umfragen.acme.at");
        var late = await AddAsync(owner, "survey.acme.at");
        SetRecords(factory, soon);
        await SurveysApiTests.ScalarAsync(factory, $"UPDATE domain SET created_at = now() - interval '8 days' WHERE id = '{late.GetProperty("id").GetString()}'");

        await factory.Services.GetRequiredService<DomainChecks>().RunOnceAsync(Cancel);
        var domains = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/domains"), Cancel);

        Assert.Equal(
            [("survey.acme.at", "failed"), ("umfragen.acme.at", "verified")],
            domains.GetProperty("items").EnumerateArray().Select(item => (item.GetProperty("hostname").GetString(), item.GetProperty("status").GetString())).OrderBy(item => item.Item1));
    }

    [Fact]
    public async Task Editors_DoNotChangeDomains()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");

        var added = await owner.PostAsJsonAsync(Url("/api/app/domains"), new { hostname = "umfragen.acme.at" }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, added.StatusCode);
    }
}
