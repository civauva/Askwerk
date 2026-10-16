// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Members of an organisation (AWK-30, W18): owners and admins invite colleagues by email or with a link valid for
/// 7 days, change roles and remove members. There is always an owner; only owners manage owners; every step is audited.
/// </summary>
public sealed partial class MembersApiTests(PostgresServer server)
{
    private const string Password = "a long enough password";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static List<string> List(params string[] items) => [.. items];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task<long> AuditCountAsync(AskwerkApiFactory factory, string action) =>
        SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM audit_log WHERE action = '{action}'").ContinueWith(task => (long)task.Result!, TaskScheduler.Default);

    private static Task<JsonElement> MembersAsync(HttpClient client) => client.GetFromJsonAsync<JsonElement>(Url("/api/app/members"), Cancel);

    /// <summary>The token at the end of an invitation link.</summary>
    private static string TokenOf(string link) => LinkToken().Match(link).Groups[1].Value;

    [GeneratedRegex(@"/join/([A-Za-z0-9_-]+)")]
    private static partial Regex LinkToken();

    /// <summary>Invites one address with the given role and returns the link to join.</summary>
    internal static async Task<string> InviteAsync(HttpClient owner, string email, string role)
    {
        var invited = await owner.PostAsJsonAsync(Url("/api/app/members/invitations"), new { emails = List(email), role }, Cancel);
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);

        return (await JsonAsync(invited)).GetProperty("invitations")[0].GetProperty("link").GetString()!;
    }

    /// <summary>A colleague who joined with a new account, signed in in their own browser.</summary>
    internal static async Task<(HttpClient Client, string UserId)> JoinedAsync(AskwerkApiFactory factory, HttpClient owner, string email, string role, string name = "Ben Huber")
    {
        var link = await InviteAsync(owner, email, role);
        var colleague = await factory.CreateReadyClientAsync();
        var joined = await colleague.PostAsJsonAsync(Url($"/api/app/invitations/{TokenOf(link)}/accept"), new { name, password = Password }, Cancel);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var me = await colleague.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        return (colleague, me.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Invite_WithoutEmail_GivesALinkValidFor7Days_AndListsItAsOpen()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var invited = await owner.PostAsJsonAsync(Url("/api/app/members/invitations"), new { emails = List("ben@acme.at", " anna@acme.at "), role = "editor" }, Cancel);
        var answer = await JsonAsync(invited);
        var members = await MembersAsync(owner);

        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        Assert.False(answer.GetProperty("sent").GetBoolean());
        Assert.Equal(["anna@acme.at"], answer.GetProperty("alreadyMembers").EnumerateArray().Select(item => item.GetString()));
        Assert.StartsWith("https://surveys.acme.at/join/", answer.GetProperty("invitations")[0].GetProperty("link").GetString());
        var open = members.GetProperty("invitations")[0];
        Assert.Equal(("ben@acme.at", "editor", "Anna Berger"), (open.GetProperty("email").GetString(), open.GetProperty("role").GetString(), open.GetProperty("invitedBy").GetString()));
        Assert.Equal(7, (open.GetProperty("expiresAt").GetDateTimeOffset() - open.GetProperty("invitedAt").GetDateTimeOffset()).Days);
        Assert.Equal(1L, await AuditCountAsync(factory, "member.invited"));
    }

    [Fact]
    public async Task Invite_WithEmail_SendsTheLink()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE instance_settings SET smtp_host = 'smtp.acme.at', smtp_port = 587, smtp_sender = 'Acme Surveys <surveys@acme.at>'");

        var invited = await owner.PostAsJsonAsync(Url("/api/app/members/invitations"), new { emails = List("ben@acme.at"), role = "viewer" }, Cancel);

        Assert.True((await JsonAsync(invited)).GetProperty("sent").GetBoolean());
        var mail = Assert.Single(factory.Emails.Sent).Message;
        Assert.Equal("ben@acme.at", mail.To);
        Assert.Contains("Acme GmbH", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("https://surveys.acme.at/join/", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_AsANewPerson_CreatesTheAccount_AndSignsIn()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var link = await InviteAsync(owner, "ben@acme.at", "editor");
        using var colleague = await factory.CreateReadyClientAsync();

        var shown = await colleague.GetFromJsonAsync<JsonElement>(Url($"/api/app/invitations/{TokenOf(link)}"), Cancel);
        var joined = await colleague.PostAsJsonAsync(Url($"/api/app/invitations/{TokenOf(link)}/accept"), new { name = "Ben Huber", password = Password }, Cancel);
        var again = await colleague.PostAsJsonAsync(Url($"/api/app/invitations/{TokenOf(link)}/accept"), new { name = "Ben Huber", password = Password }, Cancel);
        var me = await colleague.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);
        var members = await MembersAsync(owner);

        Assert.Equal(("Acme GmbH", "editor", "Anna Berger", "ben@acme.at", false), (shown.GetProperty("organisation").GetString(), shown.GetProperty("role").GetString(), shown.GetProperty("invitedBy").GetString(), shown.GetProperty("email").GetString(), shown.GetProperty("accountExists").GetBoolean()));
        Assert.Equal("signed-in", (await JsonAsync(joined)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Gone, again.StatusCode);
        Assert.Equal(("Ben Huber", "editor"), (me.GetProperty("name").GetString(), me.GetProperty("organisations")[0].GetProperty("role").GetString()));
        Assert.Equal(2, members.GetProperty("members").GetArrayLength());
        Assert.Equal(0, members.GetProperty("invitations").GetArrayLength());
        Assert.Equal(1L, await AuditCountAsync(factory, "member.joined"));
    }

    [Fact]
    public async Task Join_AsAnAdmin_AsksForTwoFactorFirst()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var link = await InviteAsync(owner, "ben@acme.at", "admin");
        using var colleague = await factory.CreateReadyClientAsync();

        var joined = await colleague.PostAsJsonAsync(Url($"/api/app/invitations/{TokenOf(link)}/accept"), new { name = "Ben Huber", password = Password }, Cancel);

        Assert.Equal("two-factor-setup-required", (await JsonAsync(joined)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Join_WithAnExistingAccount_NeedsItsSignIn_ThenAddsTheOrganisation()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (ben, _) = await JoinedAsync(factory, owner, "ben@acme.at", "editor");
        using var __ = ben;
        // A second organisation, created by SQL as setup only makes the first.
        await SurveysApiTests.ScalarAsync(factory, "INSERT INTO tenant (id, name, slug, created_at, embed_origins) VALUES ('00000000-0000-0000-0000-0000000000b2', 'Partner Ltd', 'partner', now(), '{}')");
        await SurveysApiTests.ScalarAsync(factory, "INSERT INTO membership (id, user_id, tenant_id, role, created_at) SELECT gen_random_uuid(), id, '00000000-0000-0000-0000-0000000000b2', 0, now() FROM app_user WHERE email = 'anna@acme.at'");
        (await owner.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = "00000000-0000-0000-0000-0000000000b2" }, Cancel)).EnsureSuccessStatusCode();
        var link = await InviteAsync(owner, "ben@acme.at", "viewer");
        using var stranger = await factory.CreateReadyClientAsync();

        var shown = await stranger.GetFromJsonAsync<JsonElement>(Url($"/api/app/invitations/{TokenOf(link)}"), Cancel);
        var withoutSignIn = await stranger.PostAsJsonAsync(Url($"/api/app/invitations/{TokenOf(link)}/accept"), new { name = "Someone", password = Password }, Cancel);
        var joined = await ben.PostAsJsonAsync(Url($"/api/app/invitations/{TokenOf(link)}/accept"), new { }, Cancel);
        var me = await ben.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel);

        Assert.True(shown.GetProperty("accountExists").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, withoutSignIn.StatusCode);
        Assert.Equal("account-exists", (await JsonAsync(withoutSignIn)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        Assert.Equal(["Acme GmbH", "Partner Ltd"], me.GetProperty("organisations").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ALink_ExpiresAfter7Days_AndARevokedOneNoLongerWorks()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var old = await InviteAsync(owner, "ben@acme.at", "editor");
        var revoked = await InviteAsync(owner, "clara@acme.at", "editor");
        await SurveysApiTests.ScalarAsync(factory, "UPDATE member_invitation SET expires_at = now() - interval '1 minute' WHERE email = 'ben@acme.at'");
        var openId = (await MembersAsync(owner)).GetProperty("invitations").EnumerateArray().First(item => item.GetProperty("email").GetString() == "clara@acme.at").GetProperty("id").GetString();
        (await owner.DeleteAsync(Url($"/api/app/members/invitations/{openId}"), Cancel)).EnsureSuccessStatusCode();
        using var colleague = await factory.CreateReadyClientAsync();

        var expired = await colleague.GetAsync(Url($"/api/app/invitations/{TokenOf(old)}"), Cancel);
        var gone = await colleague.GetAsync(Url($"/api/app/invitations/{TokenOf(revoked)}"), Cancel);

        Assert.Equal([HttpStatusCode.Gone, HttpStatusCode.Gone], [expired.StatusCode, gone.StatusCode]);
        Assert.Equal(1L, await AuditCountAsync(factory, "invitation.revoked"));
    }

    [Fact]
    public async Task ChangeRole_TakesEffect_AndIsAudited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (ben, benId) = await JoinedAsync(factory, owner, "ben@acme.at", "viewer");
        using var _ = ben;

        var changed = await owner.PutAsJsonAsync(Url($"/api/app/members/{benId}/role"), new { role = "editor" }, Cancel);
        var created = await ben.PostAsJsonAsync(Url("/api/app/surveys"), new { title = "Pulse", languages = List("de"), defaultLanguage = "de" }, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1L, await AuditCountAsync(factory, "member.role-changed"));
    }

    [Fact]
    public async Task Admins_ManageEveryoneButOwners_AndOthersManageNoOne()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel)).GetProperty("id").GetString();
        var (editor, editorId) = await JoinedAsync(factory, owner, "ben@acme.at", "editor");
        var (admin, adminId) = await JoinedAsync(factory, owner, "clara@acme.at", "editor", "Clara Wolf");
        using var _ = editor;
        using var __ = admin;
        // Promoted after joining: the session needs no two-factor setup for the test; roles are read on every request.
        (await owner.PutAsJsonAsync(Url($"/api/app/members/{adminId}/role"), new { role = "admin" }, Cancel)).EnsureSuccessStatusCode();

        var editorList = await editor.GetAsync(Url("/api/app/members"), Cancel);
        var adminInvitesOwner = await admin.PostAsJsonAsync(Url("/api/app/members/invitations"), new { emails = List("eva@acme.at"), role = "owner" }, Cancel);
        var adminDemotesOwner = await admin.PutAsJsonAsync(Url($"/api/app/members/{ownerId}/role"), new { role = "editor" }, Cancel);
        var adminChangesEditor = await admin.PutAsJsonAsync(Url($"/api/app/members/{editorId}/role"), new { role = "analyst" }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, editorList.StatusCode);
        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], [adminInvitesOwner.StatusCode, adminDemotesOwner.StatusCode]);
        Assert.Equal(HttpStatusCode.NoContent, adminChangesEditor.StatusCode);
    }

    [Fact]
    public async Task TheLastOwner_CannotBeChangedOrRemoved_AndNoOneChangesTheirOwnRole()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/me"), Cancel)).GetProperty("id").GetString();

        var demote = await owner.PutAsJsonAsync(Url($"/api/app/members/{ownerId}/role"), new { role = "editor" }, Cancel);
        var leave = await owner.DeleteAsync(Url($"/api/app/members/{ownerId}"), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        Assert.Equal("last-owner", (await JsonAsync(leave)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Remove_TakesThePersonOut_AndEndsTheirSessions()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (ben, benId) = await JoinedAsync(factory, owner, "ben@acme.at", "editor");
        using var _ = ben;

        var removed = await owner.DeleteAsync(Url($"/api/app/members/{benId}"), Cancel);
        var afterwards = await ben.GetAsync(Url("/api/app/surveys"), Cancel);
        var members = await MembersAsync(owner);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
        Assert.Equal(1, members.GetProperty("members").GetArrayLength());
        Assert.Equal(1L, await AuditCountAsync(factory, "member.removed"));
    }

    [Fact]
    public async Task AMember_MayLeave()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (ben, benId) = await JoinedAsync(factory, owner, "ben@acme.at", "viewer");
        using var _ = ben;

        var left = await ben.DeleteAsync(Url($"/api/app/members/{benId}"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(1, (await MembersAsync(owner)).GetProperty("members").GetArrayLength());
    }
}
