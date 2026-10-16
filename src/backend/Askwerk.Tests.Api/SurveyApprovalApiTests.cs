// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// Approving a survey from Preview (AWK-28, W16): every member may approve the saved draft as it is, with an optional
/// note; the approval is pinned to the draft's fingerprint, withdrawn only by its author, shown next to Publish and in
/// the Publish dialog, and audited. Publishing never waits for it.
/// </summary>
public sealed class SurveyApprovalApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task<long> AuditCountAsync(AskwerkApiFactory factory, string action) =>
        SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM audit_log WHERE action = '{action}'").ContinueWith(task => (long)task.Result!, TaskScheduler.Default);

    private static async Task<string> DraftAsync(HttpClient owner)
    {
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();

        return id;
    }

    private static Task<JsonElement> ApprovalsAsync(HttpClient client, string id) => client.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/approvals"), Cancel);

    [Fact]
    public async Task Approve_PinsTheApprovalToTheSavedDraft_WithItsNote()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);

        var approved = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/approvals"), new { note = " checked with the works council " }, Cancel);
        var approvals = await ApprovalsAsync(owner, id);

        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        Assert.Equal("approved", approvals.GetProperty("state").GetString());
        var item = approvals.GetProperty("items")[0];
        Assert.Equal(("checked with the works council", true, true), (item.GetProperty("note").GetString(), item.GetProperty("current").GetBoolean(), item.GetProperty("mine").GetBoolean()));
        Assert.Equal(approvals.GetProperty("fingerprint").GetString(), item.GetProperty("fingerprint").GetString());
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.approved"));
    }

    [Fact]
    public async Task AChangeAfterwards_LeavesTheApprovalOnTheEarlierDraft()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);
        (await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/approvals"), new { note = (string?)null }, Cancel)).EnsureSuccessStatusCode();

        var changed = RunnerApiTests.Definition();
        changed["defaultLanguage"] = "en";
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = changed }, Cancel)).EnsureSuccessStatusCode();
        var approvals = await ApprovalsAsync(owner, id);

        Assert.Equal("earlier", approvals.GetProperty("state").GetString());
        Assert.False(approvals.GetProperty("items")[0].GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task EveryMember_MayApprove_AndOnlyTheAuthorWithdraws()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");

        var approved = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/approvals"), new { note = (string?)null }, Cancel);
        var approvalId = (await JsonAsync(approved)).GetProperty("id").GetString();
        await SurveysApiTests.ScalarAsync(factory, "UPDATE survey_approval SET approved_by = gen_random_uuid()");
        var foreign = await owner.DeleteAsync(Url($"/api/app/surveys/{id}/approvals/{approvalId}"), Cancel);

        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task Withdraw_TakesTheApprovalBack_AndIsAudited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);
        var approved = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/approvals"), new { note = (string?)null }, Cancel);
        var approvalId = (await JsonAsync(approved)).GetProperty("id").GetString();

        var withdrawn = await owner.DeleteAsync(Url($"/api/app/surveys/{id}/approvals/{approvalId}"), Cancel);
        var approvals = await ApprovalsAsync(owner, id);

        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        Assert.Equal(("none", 0), (approvals.GetProperty("state").GetString(), approvals.GetProperty("items").GetArrayLength()));
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.approval-withdrawn"));
    }

    [Fact]
    public async Task Publishing_DoesNotWait_AndThePublishedVersionKeepsItsApprovals()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await DraftAsync(owner);
        var unapproved = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{unapproved}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/approvals"), new { note = (string?)null }, Cancel)).EnsureSuccessStatusCode();

        var published = await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel);
        var publishedUnapproved = await owner.PostAsync(Url($"/api/app/surveys/{unapproved}/publish"), null, Cancel);
        var approvals = await ApprovalsAsync(owner, id);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], [published.StatusCode, publishedUnapproved.StatusCode]);
        Assert.Equal(1, approvals.GetProperty("items")[0].GetProperty("version").GetInt32());
        Assert.Equal(1L, (long)(await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'survey.published' AND diff::jsonb ->> 'approved' = 'true'"))!);
    }
}
