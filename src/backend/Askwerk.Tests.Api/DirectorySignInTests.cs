// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Askwerk.Tests.Integration;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Askwerk.Tests.Api;

/// <summary>
/// Identity integration step 3 (AWK-12, W22): sign-in with the directory password over LDAPS, with Askwerk's own LDAP
/// client. The directory is OpenLDAP built from Ldap/Dockerfile (Alpine's packages), in the full run only
/// (ASKWERK_TEST_LDAP=1); what needs no directory runs every time.
/// </summary>
public sealed class DirectorySignInTests(PostgresServer server)
{
    private static readonly string[] HrAdmins = ["HR-Admins"];

    public static bool Enabled => Environment.GetEnvironmentVariable("ASKWERK_TEST_LDAP") == "1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static Task<HttpResponseMessage> SaveAsync(HttpClient owner, string host, int port, string? ca) =>
        owner.PutAsJsonAsync(
            Url("/api/app/sign-in"),
            new
            {
                handoverEnabled = false,
                provisioningEnabled = false,
                roleRules = new[] { new { group = HrAdmins[0], role = "admin" } },
                defaultRole = "viewer",
                ldapEnabled = true,
                ldapHost = host,
                ldapPort = port,
                ldapBindDn = "cn=reader,dc=acme,dc=test",
                ldapBindPassword = "reader-secret",
                ldapBaseDn = "ou=people,dc=acme,dc=test",
                ldapLoginAttribute = "uid",
                ldapObjectClass = "inetOrgPerson",
                ldapGroupAttribute = "ou",
                ldapCaCertificate = ca,
                ldapDisplayName = "Acme Directory",
            },
            Cancel);

    private static async Task<string> SlugAsync(HttpClient owner) =>
        (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel)).GetProperty("organisations")[0].GetProperty("slug").GetString()!;

    [Fact]
    public async Task AnUnreachableDirectory_IsReported_AndUnknownOrganisationsHaveNoOptions()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();

        var saved = await SaveAsync(owner, "localhost", port, null);
        var tried = await owner.PostAsJsonAsync(Url("/api/app/sign-in/ldap-test"), new { username = "clara", password = "x" }, Cancel);
        using var anonymous = factory.CreateClient();
        var options = await anonymous.GetFromJsonAsync<JsonElement>(Url($"/api/app/session/sso/{await SlugAsync(owner)}"), Cancel);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("directory-unreachable", (await tried.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("problem").GetString());
        Assert.Equal((true, false, "Acme Directory"), (options.GetProperty("ldap").GetBoolean(), options.GetProperty("oidc").GetBoolean(), options.GetProperty("ldapName").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(Url("/api/app/session/sso/nobody-here"), Cancel)).StatusCode);
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "The directory runs in the full run only (ASKWERK_TEST_LDAP=1).")]
    public async Task People_SignInWithTheirDirectoryPassword_AndGetTheMappedRole()
    {
        var context = Path.Combine(Snapshot.FindRepositoryRoot(), "src", "backend", "Askwerk.Tests.Api", "Ldap");
        var image = new ImageFromDockerfileBuilder().WithDockerfileDirectory(context).WithName("askwerk-test-ldap:latest").WithCleanUp(false).Build();
        await image.CreateAsync(Cancel);
        await using var directory = new ContainerBuilder(image).WithPortBinding(636, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(636)).Build();
        await directory.StartAsync(Cancel);
        var ca = Encoding.ASCII.GetString(await directory.ReadFileAsync("/certs/ca.pem", Cancel));

        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        (await SaveAsync(owner, "localhost", directory.GetMappedPublicPort(636), ca)).EnsureSuccessStatusCode();
        var slug = await SlugAsync(owner);

        var test = await (await owner.PostAsJsonAsync(Url("/api/app/sign-in/ldap-test"), new { username = "clara", password = "clara-secret" }, Cancel)).Content.ReadFromJsonAsync<JsonElement>(Cancel);
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var wrong = await browser.PostAsJsonAsync(Url("/api/app/session/ldap"), new { organisation = slug, username = "clara", password = "nope" }, Cancel);
        var noMail = await browser.PostAsJsonAsync(Url("/api/app/session/ldap"), new { organisation = slug, username = "max", password = "max-secret" }, Cancel);
        var empty = await browser.PostAsJsonAsync(Url("/api/app/session/ldap"), new { organisation = slug, username = "clara", password = "" }, Cancel);
        var signedIn = await browser.PostAsJsonAsync(Url("/api/app/session/ldap"), new { organisation = slug, username = "clara", password = "clara-secret" }, Cancel);
        var me = await browser.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        var sso = await browser.GetAsync(Url($"/sso/{slug}"), Cancel);

        Assert.True(test.GetProperty("ok").GetBoolean(), test.ToString());
        Assert.Equal(("clara@acme.test", "admin"), (test.GetProperty("email").GetString(), test.GetProperty("role").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noMail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(("clara@acme.test", "admin"), (me.GetProperty("email").GetString(), me.GetProperty("organisations")[0].GetProperty("role").GetString()));
        Assert.Equal($"/?sso={slug}", sso.Headers.Location?.OriginalString);
    }
}
