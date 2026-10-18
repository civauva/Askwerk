// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Public API: the organisation's contacts (AWK-67), scopes contacts:read and contacts:write. A CRM keeps the address
/// book in step: one idempotent PUT by External ID, else email; who unsubscribed stays unsubscribed.
/// </summary>
public sealed class PublicContactsApiTests(PostgresServer server)
{
    private static readonly string[] ReadAndWrite = ["contacts:read", "contacts:write"];
    private static readonly string[] ReadOnly = ["contacts:read"];
    private static readonly string[] ParticipantsOnly = ["participants:read", "participants:write"];
    private static readonly string[] KeyAccounts = ["Key accounts"];
    private static readonly string[] Service = ["Service"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static async Task<HttpClient> KeyAsync(AskwerkApiFactory factory, HttpClient owner, string[]? scopes = null)
    {
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner, scopes ?? ReadAndWrite);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient key, object contact) => key.PutAsJsonAsync(Url("/api/v1/contacts"), contact, Cancel);

    private static Task<object?> CountAsync(AskwerkApiFactory factory, string action) =>
        SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM audit_log WHERE action = '{action}' AND lower(actor_kind) = 'apikey'");

    [Fact]
    public async Task List_FiltersBySearchGroupAndExternalId_AndPagesOnACursor()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);
        await PutAsync(key, new { email = "lena@acme.at", name = "Lena Gruber", language = "de", externalId = "CRM-1", groups = KeyAccounts, fields = new Dictionary<string, string> { ["segment"] = "Gold" } });
        await PutAsync(key, new { email = "thomas@acme.at", name = "Thomas Berger", externalId = "CRM-2", groups = KeyAccounts });
        await PutAsync(key, new { email = "anna@acme.at", groups = Service });

        var first = await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/contacts?limit=2"), Cancel);
        var second = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/contacts?limit=2&cursor={first.GetProperty("nextCursor").GetString()}"), Cancel);
        var searched = await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/contacts?search=gruber"), Cancel);
        var grouped = await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/contacts?group=Key%20accounts"), Cancel);
        var byExternalId = await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/contacts?externalId=CRM-1"), Cancel);

        Assert.Equal(["lena@acme.at", "thomas@acme.at"], first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        Assert.Equal(["anna@acme.at"], second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(["lena@acme.at"], searched.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        Assert.Equal(["lena@acme.at", "thomas@acme.at"], grouped.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        var lena = byExternalId.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(("Lena Gruber", "de", "CRM-1", "Gold"), (lena.GetProperty("name").GetString(), lena.GetProperty("language").GetString(), lena.GetProperty("externalId").GetString(), lena.GetProperty("fields").GetProperty("segment").GetString()));
        Assert.Equal(KeyAccounts, lena.GetProperty("groups").EnumerateArray().Select(group => group.GetString()));
        Assert.Equal((false, false), (lena.GetProperty("unsubscribed").GetBoolean(), lena.GetProperty("bounced").GetBoolean()));
        Assert.Equal(JsonValueKind.String, lena.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.String, lena.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Put_ByExternalId_FindsTheSameContact_WhenTheAddressChanged_AndIsIdempotent()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);

        var created = await PutAsync(key, new { email = "lena@acme.at", name = "Lena", externalId = "CRM-1" });
        var moved = await PutAsync(key, new { email = "Lena.Gruber@acme.at", name = "Lena Gruber", externalId = "CRM-1" });
        var again = await PutAsync(key, new { email = "lena.gruber@acme.at", name = "Lena Gruber", externalId = "CRM-1" });
        var list = await key.GetFromJsonAsync<JsonElement>(Url("/api/v1/contacts"), Cancel);

        var id = (await JsonAsync(created)).GetProperty("id").GetString();
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK), (created.StatusCode, moved.StatusCode, again.StatusCode));
        Assert.Equal($"/api/v1/contacts/{id}", created.Headers.Location?.OriginalString);
        var contact = await JsonAsync(again);
        Assert.Equal((id, "lena.gruber@acme.at", "Lena Gruber"), (contact.GetProperty("id").GetString(), contact.GetProperty("email").GetString(), contact.GetProperty("name").GetString()));
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
        Assert.Equal((1L, 2L), (await CountAsync(factory, "contact.added"), await CountAsync(factory, "contact.changed")));
    }

    [Fact]
    public async Task Put_ByEmail_UpdatesTheContact_AndKeepsWhatIsNotSent()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);

        var created = await JsonAsync(await PutAsync(key, new { email = "thomas@acme.at", name = "Thomas", language = "en", groups = KeyAccounts }));
        var updated = await PutAsync(key, new { email = "THOMAS@acme.at", fields = new Dictionary<string, string> { ["department"] = "Sales" } });
        var read = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/contacts/{created.GetProperty("id").GetString()}"), Cancel);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(("Thomas", "en", "Sales"), (read.GetProperty("name").GetString(), read.GetProperty("language").GetString(), read.GetProperty("fields").GetProperty("department").GetString()));
        Assert.Equal(KeyAccounts, read.GetProperty("groups").EnumerateArray().Select(group => group.GetString()));
    }

    [Fact]
    public async Task Put_NeverSubscribesAgain_WhoUnsubscribed_EvenWithANewAddress()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);
        var id = (await JsonAsync(await PutAsync(key, new { email = "lena@acme.at", externalId = "CRM-1" }))).GetProperty("id").GetString();
        await SurveysApiTests.ScalarAsync(factory, "INSERT INTO unsubscribe (id, tenant_id, email, created_at) SELECT gen_random_uuid(), tenant_id, email, now() FROM contact WHERE email = 'lena@acme.at'");

        var same = await JsonAsync(await PutAsync(key, new { email = "lena@acme.at", externalId = "CRM-1", unsubscribed = false }));
        var moved = await JsonAsync(await PutAsync(key, new { email = "lena.gruber@acme.at", externalId = "CRM-1" }));
        var read = await key.GetFromJsonAsync<JsonElement>(Url($"/api/v1/contacts/{id}"), Cancel);

        Assert.True(same.GetProperty("unsubscribed").GetBoolean());
        Assert.True(moved.GetProperty("unsubscribed").GetBoolean());
        Assert.True(read.GetProperty("unsubscribed").GetBoolean());
        Assert.Equal(2L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM unsubscribe"));
    }

    [Fact]
    public async Task Put_RefusesBadInput_AndAnAddressOfAnotherContact()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);
        await PutAsync(key, new { email = "lena@acme.at", externalId = "CRM-1" });
        await PutAsync(key, new { email = "thomas@acme.at", externalId = "CRM-2" });

        var invalid = await PutAsync(key, new { email = "no address", language = "deutsch" });
        var taken = await PutAsync(key, new { email = "thomas@acme.at", externalId = "CRM-1" });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        var problem = await JsonAsync(invalid);
        Assert.Equal("https://askwerk.org/problems/contact-invalid", problem.GetProperty("type").GetString());
        Assert.Equal("email-invalid", problem.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("language-invalid", problem.GetProperty("errors").GetProperty("language")[0].GetString());
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("https://askwerk.org/problems/contact-email-taken", (await JsonAsync(taken)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Delete_RemovesTheContact_AndIsAuditedWithTheKey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);
        var id = (await JsonAsync(await PutAsync(key, new { email = "lena@acme.at" }))).GetProperty("id").GetString();

        var deleted = await key.DeleteAsync(Url($"/api/v1/contacts/{id}"), Cancel);
        var read = await key.GetAsync(Url($"/api/v1/contacts/{id}"), Cancel);
        var again = await key.DeleteAsync(Url($"/api/v1/contacts/{id}"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal("https://askwerk.org/problems/contact-not-found", (await JsonAsync(read)).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(1L, await CountAsync(factory, "contact.removed"));
    }

    [Fact]
    public async Task AReadKey_CannotWrite_AndAKeyWithoutContactScopes_CannotRead()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var writer = await KeyAsync(factory, owner);
        using var reader = await KeyAsync(factory, owner, ReadOnly);
        using var other = await KeyAsync(factory, owner, ParticipantsOnly);
        var id = (await JsonAsync(await PutAsync(writer, new { email = "lena@acme.at" }))).GetProperty("id").GetString();

        var read = await reader.GetAsync(Url($"/api/v1/contacts/{id}"), Cancel);
        var put = await PutAsync(reader, new { email = "thomas@acme.at" });
        var delete = await reader.DeleteAsync(Url($"/api/v1/contacts/{id}"), Cancel);
        var list = await other.GetAsync(Url("/api/v1/contacts"), Cancel);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (put.StatusCode, delete.StatusCode, list.StatusCode));
        Assert.Equal("https://askwerk.org/problems/scope-missing", (await JsonAsync(put)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task AnotherOrganisation_DoesNotSeeOrChangeTheContacts()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var key = await KeyAsync(factory, owner);
        var id = (await JsonAsync(await PutAsync(key, new { email = "lena@acme.at", externalId = "CRM-1" }))).GetProperty("id").GetString();
        var other = Guid.CreateVersion7();
        await SurveysApiTests.ScalarAsync(factory, $"""
            INSERT INTO tenant (id, name, slug, created_at) VALUES ('{other}', 'Acme Schweiz AG', 'acme-ch', now());
            INSERT INTO membership (id, tenant_id, user_id, role, created_at) SELECT gen_random_uuid(), '{other}', id, 'Owner', now() FROM app_user;
            """);
        (await owner.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = other }, Cancel)).EnsureSuccessStatusCode();
        using var stranger = await KeyAsync(factory, owner);

        var list = await stranger.GetFromJsonAsync<JsonElement>(Url("/api/v1/contacts"), Cancel);
        var read = await stranger.GetAsync(Url($"/api/v1/contacts/{id}"), Cancel);
        var delete = await stranger.DeleteAsync(Url($"/api/v1/contacts/{id}"), Cancel);
        var created = await PutAsync(stranger, new { email = "lena@acme.at", externalId = "CRM-1" });

        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (read.StatusCode, delete.StatusCode));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(2L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM contact"));
    }
}
