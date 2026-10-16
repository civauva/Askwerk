// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// Survey settings → Collecting responses (AWK-29, W24): a closing date entered in the instance's time zone, after
/// which the runner refuses responses and a job closes the survey; and one response per browser on the open link.
/// </summary>
public sealed class CollectingSettingsApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task<object?> AuditCountAsync(AskwerkApiFactory factory, string action) =>
        SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM audit_log WHERE action = '{action}'");

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    /// <summary>A published survey with its owner and a fresh browser for a respondent.</summary>
    private static async Task<(HttpClient Owner, string Id, HttpClient Respondent)> PublishedAsync(AskwerkApiFactory factory)
    {
        var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return (owner, id, await factory.CreateReadyClientAsync());
    }

    private static async Task AnswerAndFinishAsync(HttpClient respondent, string id)
    {
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 4 } } }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p2"), new { answers = new { q3 = new { option = "1" } } }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel)).EnsureSuccessStatusCode();
    }

    /// <summary>Moves the closing date and its job into the past, as if the time had come.</summary>
    private static async Task LetTheClosingDatePassAsync(AskwerkApiFactory factory)
    {
        await SurveysApiTests.ScalarAsync(factory, "UPDATE survey SET closes_at = now() - interval '1 minute'");
        await SurveysApiTests.ScalarAsync(factory, "UPDATE job SET run_at = now() - interval '1 minute' WHERE kind = 'close-survey'");
    }

    [Fact]
    public async Task ClosingDate_IsEnteredInTheInstanceTimeZone_AndShownInTheSummary()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;

        var set = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal(new DateTimeOffset(2099, 7, 1, 16, 0, 0, TimeSpan.Zero), summary.GetProperty("closesAt").GetDateTimeOffset());
        Assert.Equal(("Europe/Vienna", "published"), (summary.GetProperty("timeZone").GetString(), summary.GetProperty("status").GetString()));
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.closing-date-set"));
    }

    [Fact]
    public async Task ClosingDate_InThePast_IsRefusedAtTheField()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;

        var set = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2001-01-01T00:00" }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, set.StatusCode);
        Assert.Equal("closing-date-past", (await JsonAsync(set)).GetProperty("errors").GetProperty("closesAt")[0].GetString());
    }

    [Fact]
    public async Task ClosingDate_Removed_CollectsWithoutEnd()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;
        await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);

        var removed = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = (string?)null }, Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("closesAt").ValueKind);
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.closing-date-removed"));
    }

    [Fact]
    public async Task ClosingDate_OncePassed_TheRunnerRefusesAndSaysWhen()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, respondent) = await PublishedAsync(factory);
        using var _owner = owner;
        using var _respondent = respondent;
        using var unfinished = await factory.CreateReadyClientAsync();
        await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);
        (await unfinished.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();
        await LetTheClosingDatePassAsync(factory);

        var opened = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);
        var started = await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel);
        var saved = await unfinished.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 4 } } }, Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal("closed", opened.GetProperty("survey").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, opened.GetProperty("survey").GetProperty("closedAt").ValueKind);
        Assert.Equal("Europe/Vienna", opened.GetProperty("survey").GetProperty("timeZone").GetString());
        Assert.Equal(("closed", "closed"), ((await JsonAsync(started)).GetProperty("code").GetString(), (await JsonAsync(saved)).GetProperty("code").GetString()));
        Assert.Equal("closed", summary.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ClosingDate_OncePassed_TheJobClosesTheSurvey_AsTheSystem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;
        await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);
        await LetTheClosingDatePassAsync(factory);

        await RunJobsAsync(factory);

        Assert.Equal("closed", await SurveysApiTests.ScalarAsync(factory, "SELECT status FROM survey"));
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'survey.closed' AND lower(actor_kind) = 'system' AND diff LIKE '%closing-date%'"));
    }

    [Fact]
    public async Task Reopen_AfterTheClosingDate_RemovesIt()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;
        await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);
        await LetTheClosingDatePassAsync(factory);

        var reopened = await owner.PostAsync(Url($"/api/app/surveys/{id}/reopen"), null, Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, reopened.StatusCode);
        Assert.Equal(("published", JsonValueKind.Null), (summary.GetProperty("status").GetString(), summary.GetProperty("closesAt").ValueKind));
    }

    [Fact]
    public async Task OneResponsePerPerson_IsOn_ABrowserThatFinishedCannotStartAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, respondent) = await PublishedAsync(factory);
        using var _owner = owner;
        using var _respondent = respondent;
        await AnswerAndFinishAsync(respondent, id);

        var again = await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel);
        var opened = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}"), Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already-answered", (await JsonAsync(again)).GetProperty("code").GetString());
        Assert.True(opened.GetProperty("survey").GetProperty("oneResponsePerPerson").GetBoolean());
        Assert.True(summary.GetProperty("oneResponsePerPerson").GetBoolean());
    }

    [Fact]
    public async Task OneResponsePerPerson_TheBrowserRemembersItForAYear()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, respondent) = await PublishedAsync(factory);
        using var _owner = owner;
        using var _respondent = respondent;
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();
        await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 4 } } }, Cancel);
        await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p2"), new { answers = new { q3 = new { option = "1" } } }, Cancel);

        var completed = await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel);

        Assert.Contains("max-age=31536000", Assert.Single(completed.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OneResponsePerPerson_TurnedOff_TheSameBrowserAnswersAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, respondent) = await PublishedAsync(factory);
        using var _owner = owner;
        using var _respondent = respondent;
        await AnswerAndFinishAsync(respondent, id);

        var off = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/one-response"), new { on = false }, Cancel);
        var again = await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.Created), (off.StatusCode, again.StatusCode));
        Assert.Equal(1L, await AuditCountAsync(factory, "survey.one-response-changed"));
    }

    [Fact]
    public async Task Viewers_CannotChangeTheCollectingSettings()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");

        var date = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);
        var one = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/one-response"), new { on = false }, Cancel);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (date.StatusCode, one.StatusCode));
    }

    [Fact]
    public async Task AnotherOrganisation_DoesNotFindTheSurvey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id, _) = await PublishedAsync(factory);
        using var _owner = owner;
        var other = Guid.CreateVersion7();
        await SurveysApiTests.ScalarAsync(factory, $"""
            INSERT INTO tenant (id, name, slug, created_at) VALUES ('{other}', 'Acme Schweiz AG', 'acme-ch', now());
            INSERT INTO membership (id, tenant_id, user_id, role, created_at) SELECT gen_random_uuid(), '{other}', id, 'Owner', now() FROM app_user;
            """);
        (await owner.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = other }, Cancel)).EnsureSuccessStatusCode();

        var date = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/closing-date"), new { closesAt = "2099-07-01T18:00" }, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, date.StatusCode);
    }
}
