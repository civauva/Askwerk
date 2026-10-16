// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Administration → Organisation and This Askwerk (AWK-38, W29): the organisation, every organisation, administrators.</summary>
public sealed class OrganisationApiTests(PostgresServer server)
{
    private static readonly string[] ReadScopes = ["surveys:read"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    [Fact]
    public async Task Organisation_IsRead_Renamed_AndItsShortNameChecked()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var before = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/organisation"), Cancel);
        var renamed = await owner.PutAsJsonAsync(Url("/api/app/organisation"), new { name = "Acme Holding", slug = "acme-holding" }, Cancel);
        var invalid = await owner.PutAsJsonAsync(Url("/api/app/organisation"), new { name = "Acme", slug = "Not valid!" }, Cancel);
        var me = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        Assert.Equal(("owner", 1), (before.GetProperty("role").GetString(), before.GetProperty("members").GetInt32()));
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(("Acme Holding", "acme-holding"), (me.GetProperty("organisations")[0].GetProperty("name").GetString(), me.GetProperty("organisations")[0].GetProperty("slug").GetString()));
        Assert.True(me.GetProperty("installationAdmin").GetBoolean());
    }

    [Fact]
    public async Task Ownership_GoesToAnotherMember_AndTheOldOwnerBecomesAdmin()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (colleague, userId) = await MembersApiTests.JoinedAsync(factory, owner, "ben@acme.at", "editor");
        using var _ = colleague;

        var refused = await colleague.PostAsJsonAsync(Url("/api/app/organisation/transfer"), new { userId }, Cancel);
        var moved = await owner.PostAsJsonAsync(Url("/api/app/organisation/transfer"), new { userId }, Cancel);
        var mine = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/organisation"), Cancel);
        var theirs = await colleague.GetFromJsonAsync<JsonElement>(Url("/api/app/organisation"), Cancel);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.NoContent), (refused.StatusCode, moved.StatusCode));
        Assert.Equal(("admin", "owner"), (mine.GetProperty("role").GetString(), theirs.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task Installation_ListsCreatesSuspendsAndDeletesOrganisations_ForItsAdministratorsOnly()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (colleague, _) = await MembersApiTests.JoinedAsync(factory, owner, "ben@acme.at", "editor");
        using var __ = colleague;

        var notAdmin = await colleague.GetAsync(Url("/api/app/installation/organisations"), Cancel);
        var invited = await owner.PostAsJsonAsync(Url("/api/app/installation/organisations"), new { name = "Falke & Co", ownerEmail = "clara@falke.at" }, Cancel);
        var joined = await owner.PostAsJsonAsync(Url("/api/app/installation/organisations"), new { name = "Acme HR Services", slug = "acme-hr", ownerEmail = "ben@acme.at" }, Cancel);
        var taken = await owner.PostAsJsonAsync(Url("/api/app/installation/organisations"), new { name = "Again", slug = "acme-hr", ownerEmail = "x@y.at" }, Cancel);
        var joinedBody = await JsonAsync(joined);
        var hr = joinedBody.GetProperty("id").GetString();
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/installation/organisations"), Cancel);
        var suspended = await owner.PostAsync(Url($"/api/app/installation/organisations/{hr}/suspend"), null, Cancel);
        var benWhileSuspended = await colleague.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        var switched = await colleague.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = hr }, Cancel);
        await owner.PostAsync(Url($"/api/app/installation/organisations/{hr}/resume"), null, Cancel);
        var switchedAfter = await colleague.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = hr }, Cancel);
        var deleted = await owner.DeleteAsync(Url($"/api/app/installation/organisations/{hr}"), Cancel);
        var benAfterDelete = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/installation/organisations"), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
        var invitation = await JsonAsync(invited);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        Assert.False(invitation.GetProperty("ownerJoined").GetBoolean());
        Assert.StartsWith("https://surveys.acme.at/join/", invitation.GetProperty("invitationLink").GetString());
        Assert.True(joinedBody.GetProperty("ownerJoined").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(["Acme GmbH", "Falke & Co", "Acme HR Services"], list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("Ben Huber", list.GetProperty("items")[2].GetProperty("owner").GetString());
        Assert.Equal(HttpStatusCode.NoContent, suspended.StatusCode);
        Assert.Contains(benWhileSuspended.GetProperty("organisations").EnumerateArray(), item => item.GetProperty("id").GetString() == hr && item.GetProperty("status").GetString() == "suspended");
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.OK), (switched.StatusCode, switchedAfter.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var gone = benAfterDelete.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == hr);
        Assert.Equal("deleted", gone.GetProperty("status").GetString());
        Assert.True(gone.GetProperty("removesAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task DeletedOrganisation_IsRestored_OrItsDataRemovedOnceTheShortNameConfirms()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "BI", scopes = ReadScopes }, Cancel)).EnsureSuccessStatusCode();
        var tenant = (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/organisation"), Cancel)).GetProperty("id").GetString();

        (await owner.DeleteAsync(Url($"/api/app/installation/organisations/{tenant}"), Cancel)).EnsureSuccessStatusCode();
        var restored = await owner.PostAsync(Url($"/api/app/installation/organisations/{tenant}/restore"), null, Cancel);
        var restoredAgain = await owner.PostAsync(Url($"/api/app/installation/organisations/{tenant}/restore"), null, Cancel);
        var countsWhileActive = await owner.GetAsync(Url($"/api/app/installation/organisations/{tenant}/removal"), Cancel);
        (await owner.DeleteAsync(Url($"/api/app/installation/organisations/{tenant}"), Cancel)).EnsureSuccessStatusCode();
        var counts = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/installation/organisations/{tenant}/removal"), Cancel);
        var mistyped = await owner.PostAsJsonAsync(Url($"/api/app/installation/organisations/{tenant}/remove"), new { slug = "acm" }, Cancel);
        var removed = await owner.PostAsJsonAsync(Url($"/api/app/installation/organisations/{tenant}/remove"), new { slug = "acme" }, Cancel);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/installation/organisations"), Cancel);
        var restoreAfter = await owner.PostAsync(Url($"/api/app/installation/organisations/{tenant}/restore"), null, Cancel);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (restored.StatusCode, restoredAgain.StatusCode, countsWhileActive.StatusCode));
        Assert.Equal((1, 1), (counts.GetProperty("surveys").GetInt32(), counts.GetProperty("members").GetInt32()));
        Assert.True(counts.GetProperty("other").GetInt32() >= 1);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.NoContent), (mistyped.StatusCode, removed.StatusCode));
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == tenant);
        Assert.Equal(HttpStatusCode.NotFound, restoreAfter.StatusCode);
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM survey WHERE id = '{surveyId}'"));
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM membership WHERE tenant_id = '{tenant}'"));
        Assert.StartsWith("removed-", (string?)await SurveysApiTests.ScalarAsync(factory, $"SELECT slug FROM tenant WHERE id = '{tenant}'"), StringComparison.Ordinal);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM audit_log WHERE action = 'organisation.removed' AND tenant_id = '{tenant}'"));
    }

    [Fact]
    public async Task DeletedOrganisation_IsRemovedByItself_After30Days()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var lab = (await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/installation/organisations"), new { name = "Test lab", slug = "test", ownerEmail = "anna@acme.at" }, Cancel))).GetProperty("id").GetString();
        var young = (await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/installation/organisations"), new { name = "Young", slug = "young", ownerEmail = "anna@acme.at" }, Cancel))).GetProperty("id").GetString();
        (await owner.DeleteAsync(Url($"/api/app/installation/organisations/{lab}"), Cancel)).EnsureSuccessStatusCode();
        (await owner.DeleteAsync(Url($"/api/app/installation/organisations/{young}"), Cancel)).EnsureSuccessStatusCode();
        await SurveysApiTests.ScalarAsync(factory, $"UPDATE tenant SET deleted_at = now() - interval '31 days' WHERE id = '{lab}' RETURNING 1");

        var removed = await factory.Services.GetRequiredService<Askwerk.Api.App.Installation.TenantRemoval>().RemoveDueAsync(Cancel);
        var reused = await owner.PostAsJsonAsync(Url("/api/app/installation/organisations"), new { name = "Test lab again", slug = "test", ownerEmail = "anna@acme.at" }, Cancel);

        Assert.Equal(1, removed);
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM membership WHERE tenant_id = '{lab}'"));
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM membership WHERE tenant_id = '{young}'"));
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
    }

    [Fact]
    public async Task InstallationSettings_AreSaved_EmailTested_AndWhatTheConfigurationSetsStaysLocked()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        factory.Configuration["ASKWERK_TIME_ZONE"] = "Europe/Berlin";
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var invalid = await owner.PutAsJsonAsync(Url("/api/app/installation"), new { name = "Acme Surveys", defaultLanguage = "fr", timeZone = "Europe/Vienna", smtpHost = "smtp.acme.at", smtpSender = "nope" }, Cancel);
        var saved = await owner.PutAsJsonAsync(Url("/api/app/installation"), new { name = "Acme Surveys", defaultLanguage = "de", timeZone = "Europe/Vienna", smtpHost = "smtp.acme.at", smtpPort = 587, smtpUserName = "surveys@acme.at", smtpPassword = "s3cret", smtpSender = "Acme Surveys <surveys@acme.at>" }, Cancel);
        var settings = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/installation"), Cancel);
        var tested = await owner.PostAsJsonAsync(Url("/api/app/installation/test-email"), new { host = "smtp.acme.at", port = 587, userName = "surveys@acme.at", sender = "surveys@acme.at" }, Cancel);
        var keptPassword = await owner.PutAsJsonAsync(Url("/api/app/installation"), new { name = "Acme Surveys", defaultLanguage = "de", timeZone = "Europe/Vienna", smtpHost = "smtp.acme.at", smtpUserName = "surveys@acme.at", smtpSender = "surveys@acme.at" }, Cancel);
        var after = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/installation"), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await JsonAsync(invalid)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("defaultLanguage", out _) && errors.TryGetProperty("smtpSender", out _));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(("Acme Surveys", "de", "Europe/Berlin", "smtp.acme.at", true), (settings.GetProperty("name").GetString(), settings.GetProperty("defaultLanguage").GetString(), settings.GetProperty("timeZone").GetString(), settings.GetProperty("smtpHost").GetString(), settings.GetProperty("smtpPasswordSet").GetBoolean()));
        Assert.Equal(["baseAddress", "timeZone"], settings.GetProperty("locked").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var (smtp, message) = factory.Emails.Sent[^1];
        Assert.Equal(("anna@acme.at", "s3cret"), (message.To, smtp.Password));
        Assert.Equal(HttpStatusCode.NoContent, keptPassword.StatusCode);
        Assert.True(after.GetProperty("smtpPasswordSet").GetBoolean());
    }

    [Fact]
    public async Task SuspendedOrganisation_ClosesItsSessions_Keys_AndSurveyLinks()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{surveyId}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        var key = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/api-keys"), new { name = "BI", scopes = ReadScopes }, Cancel));
        var tenant = (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/organisation"), Cancel)).GetProperty("id").GetString();
        using var integration = await factory.CreateReadyClientAsync();
        integration.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.GetProperty("secret").GetString());
        using var respondent = await factory.CreateReadyClientAsync();

        var openBefore = await respondent.GetAsync(Url($"/api/app/run/{surveyId}"), Cancel);
        (await owner.PostAsync(Url($"/api/app/installation/organisations/{tenant}/suspend"), null, Cancel)).EnsureSuccessStatusCode();
        var run = await respondent.GetAsync(Url($"/api/app/run/{surveyId}"), Cancel);
        var api = await integration.GetAsync(Url("/api/v1/surveys"), Cancel);
        var session = await owner.GetAsync(Url("/api/app/organisation"), Cancel);
        var stillAdmin = await owner.GetAsync(Url("/api/app/installation/organisations"), Cancel);

        Assert.Equal(HttpStatusCode.OK, openBefore.StatusCode);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Conflict, HttpStatusCode.OK), (run.StatusCode, api.StatusCode, session.StatusCode, stillAdmin.StatusCode));
    }

    [Fact]
    public async Task Administrators_AreAdded_Removed_AndTheLastOneStays()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (colleague, userId) = await MembersApiTests.JoinedAsync(factory, owner, "ben@acme.at", "editor");
        using var _ = colleague;

        var unknown = await owner.PostAsJsonAsync(Url("/api/app/installation/administrators"), new { email = "nobody@acme.at" }, Cancel);
        var added = await owner.PostAsJsonAsync(Url("/api/app/installation/administrators"), new { email = "Ben@acme.at" }, Cancel);
        var benSees = await colleague.GetAsync(Url("/api/app/installation"), Cancel);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/installation/administrators"), Cancel);
        var anna = list.GetProperty("items").EnumerateArray().First(item => item.GetProperty("me").GetBoolean()).GetProperty("id").GetString();
        var removed = await owner.DeleteAsync(Url($"/api/app/installation/administrators/{userId}"), Cancel);
        var last = await owner.DeleteAsync(Url($"/api/app/installation/administrators/{anna}"), Cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.OK), (unknown.StatusCode, added.StatusCode, benSees.StatusCode));
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.Conflict), (removed.StatusCode, last.StatusCode));
    }
}
