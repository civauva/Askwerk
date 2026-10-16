// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// Invitations (AWK-7, W12 C and D): addresses pasted or from a file, personal links sent in the background with one
/// reminder, a link to unsubscribe, and who got what. Jobs run when a test asks (ASKWERK_JOBS=manual).
/// </summary>
public sealed class InvitationsApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static object Person(string email, string? name = null, string? language = null) => new { email, name, language };

    private static async Task<(HttpClient Owner, string SurveyId)> PublishedAsync(AskwerkApiFactory factory, bool smtp = true)
    {
        var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        if (smtp)
        {
            await SurveysApiTests.ScalarAsync(factory, "UPDATE instance_settings SET smtp_host = 'smtp.acme.at', smtp_port = 587, smtp_sender = 'Acme Surveys <surveys@acme.at>'");
        }

        return (owner, id);
    }

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    private static async Task<JsonElement> ParticipantsAsync(HttpClient owner, string id) =>
        await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants"), Cancel);

    private static Dictionary<string, string> Statuses(JsonElement participants) =>
        participants.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("email").GetString()!, item => item.GetProperty("status").GetString()!);

    private static Dictionary<string, string> Ids(JsonElement participants) =>
        participants.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("email").GetString()!, item => item.GetProperty("id").GetString()!);

    private static string TokenOf(string body) => body.Split("?t=")[1][..32];

    [Fact]
    public async Task Check_Paste_NamesNewDuplicateAndBadLines()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;

        var check = await JsonAsync(await owner.PostAsJsonAsync(
            Url($"/api/app/surveys/{id}/participants/check"),
            new { text = "anna.berger@acme.at\r\nJosef Huber <Josef.Huber@ACME.at>\n\nmaria@acme\nanna.berger@acme.at\n" },
            Cancel));

        Assert.Equal(
            [("anna.berger@acme.at", ""), ("josef.huber@acme.at", "Josef Huber")],
            check.GetProperty("participants").EnumerateArray().Select(person => (person.GetProperty("email").GetString(), person.GetProperty("name").GetString() ?? "")));
        Assert.Equal(1, check.GetProperty("duplicates").GetInt32());
        Assert.Equal((4, "maria@acme"), (check.GetProperty("invalid")[0].GetProperty("line").GetInt32(), check.GetProperty("invalid")[0].GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Check_CsvAndExcel_ReadTheColumnsByTheirHeader()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;

        using var csv = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Name;E-Mail;Sprache\r\nAnna Berger;anna@acme.at;en\r\nJosef Huber;josef@acme.at;\r\n")), "file", "list.csv" } };
        using var excel = new MultipartFormDataContent { { new ByteArrayContent(Workbook(["email", "name"], ["lena@acme.at", "Lena Koch"])), "file", "list.xlsx" } };
        var fromCsv = await JsonAsync(await owner.PostAsync(Url($"/api/app/surveys/{id}/participants/check"), csv, Cancel));
        var fromExcel = await JsonAsync(await owner.PostAsync(Url($"/api/app/surveys/{id}/participants/check"), excel, Cancel));

        Assert.Equal(
            [("anna@acme.at", "Anna Berger", "en"), ("josef@acme.at", "Josef Huber", "")],
            fromCsv.GetProperty("participants").EnumerateArray().Select(person => (person.GetProperty("email").GetString(), person.GetProperty("name").GetString(), person.GetProperty("language").GetString() ?? "")));
        Assert.Equal(("lena@acme.at", "Lena Koch"), (fromExcel.GetProperty("participants")[0].GetProperty("email").GetString(), fromExcel.GetProperty("participants")[0].GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Invite_SendsPersonalLinks_InTheirLanguage_WithAWayToUnsubscribe()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;

        var invited = await owner.PostAsJsonAsync(
            Url($"/api/app/surveys/{id}/invitations"),
            new { participants = new[] { Person("anna@acme.at", "Anna Berger", "en"), Person("josef@acme.at", "Josef Huber") }, deadline = "2026-10-28", reminder = true, send = true },
            Cancel);
        var before = await ParticipantsAsync(owner, id);
        await RunJobsAsync(factory);
        var after = await ParticipantsAsync(owner, id);

        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        Assert.Equal(2, (await JsonAsync(invited)).GetProperty("invited").GetInt32());
        Assert.Equal("waiting", Statuses(before)["anna@acme.at"]);
        Assert.Equal(new Dictionary<string, string> { ["anna@acme.at"] = "sent", ["josef@acme.at"] = "sent" }, Statuses(after));
        var anna = factory.Emails.Sent.Single(sent => sent.Message.To == "anna@acme.at").Message;
        var josef = factory.Emails.Sent.Single(sent => sent.Message.To == "josef@acme.at").Message;
        var code = (string)(await SurveysApiTests.ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{id}'"))!;
        Assert.Contains("Hello Anna Berger,", anna.Body, StringComparison.Ordinal);
        Assert.Contains("28 October 2026", anna.Body, StringComparison.Ordinal);
        Assert.Contains($"/s/{code}?t=", anna.Body, StringComparison.Ordinal);
        Assert.Contains("Unsubscribe", anna.Body, StringComparison.Ordinal);
        Assert.Contains("Guten Tag Josef Huber,", josef.Body, StringComparison.Ordinal);
        Assert.NotEqual(TokenOf(anna.Body), TokenOf(josef.Body));
        Assert.Equal("2026-10-25", after.GetProperty("reminderAt").GetString()![..10]);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'participants.invited'"));
    }

    [Fact]
    public async Task PersonalLink_KeepsTheResponseWithThePerson_AndTheReminderSkipsThem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at"), Person("josef@acme.at") }, reminder = true, send = true }, Cancel);
        await RunJobsAsync(factory);
        var token = TokenOf(factory.Emails.Sent.Single(sent => sent.Message.To == "anna@acme.at").Message.Body);
        using var respondent = await factory.CreateReadyClientAsync();

        var opened = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/{id}?t={token}"), Cancel);
        var opening = Statuses(await ParticipantsAsync(owner, id))["anna@acme.at"];
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "de", token }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 4 } } }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p2"), new { answers = new { q3 = new { option = "1" } } }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PostAsync(Url($"/api/app/run/{id}/response/complete"), null, Cancel)).EnsureSuccessStatusCode();
        factory.Emails.Sent.Clear();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/participants/remind"), null, Cancel)).EnsureSuccessStatusCode();
        await RunJobsAsync(factory);
        var participants = await ParticipantsAsync(owner, id);

        Assert.Equal(JsonValueKind.Null, opened.GetProperty("response").ValueKind);
        Assert.Equal("opened", opening);
        Assert.Equal("answered", Statuses(participants)["anna@acme.at"]);
        Assert.Equal(["josef@acme.at"], factory.Emails.Sent.Select(sent => sent.Message.To));
        Assert.Equal(1, participants.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("email").GetString() == "josef@acme.at").GetProperty("reminders").GetInt32());
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM response WHERE participant_id IS NOT NULL"));
    }

    [Fact]
    public async Task Unsubscribe_StopsEveryInvitationOfTheOrganisation()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at") }, send = true }, Cancel);
        await RunJobsAsync(factory);
        var token = TokenOf(factory.Emails.Sent.Single().Message.Body);
        using var respondent = await factory.CreateReadyClientAsync();

        var page = await respondent.GetFromJsonAsync<JsonElement>(Url($"/api/app/run/unsubscribe/{token}"), Cancel);
        var done = await respondent.PostAsync(Url($"/api/app/run/unsubscribe/{token}"), null, Cancel);
        var again = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/participants/check"), new { text = "anna@acme.at" }, Cancel));

        Assert.Equal(("Acme GmbH", "a***@acme.at"), (page.GetProperty("organisation").GetString(), page.GetProperty("email").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        Assert.Equal("unsubscribed", Statuses(await ParticipantsAsync(owner, id))["anna@acme.at"]);
        Assert.Equal((0, 1), (again.GetProperty("participants").GetArrayLength(), again.GetProperty("unsubscribed").GetInt32()));
    }

    [Fact]
    public async Task Bounced_IsShown_AndNotRemindedAgain()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        factory.Emails.FailFor.Add("old@acme.at");

        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("old@acme.at"), Person("anna@acme.at") }, send = true }, Cancel);
        await RunJobsAsync(factory);

        Assert.Equal(new Dictionary<string, string> { ["old@acme.at"] = "bounced", ["anna@acme.at"] = "sent" }, Statuses(await ParticipantsAsync(owner, id)));
    }

    [Fact]
    public async Task WithoutEmail_LinksCanStillBeMade_AndDownloaded()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory, smtp: false);
        using var _ = owner;

        var refused = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at", "Anna Berger") }, send = true }, Cancel);
        var linksOnly = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at", "Anna Berger") }, send = false }, Cancel);
        var csv = await owner.GetStringAsync(Url($"/api/app/surveys/{id}/participants/links.csv"), Cancel);

        Assert.Equal("email-not-set-up", (await JsonAsync(refused)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Accepted, linksOnly.StatusCode);
        Assert.StartsWith("email,name,language,link\r\nanna@acme.at,Anna Berger,,https://surveys.acme.at/s/", csv.TrimStart('﻿'), StringComparison.Ordinal);
        Assert.Equal("link-only", Statuses(await ParticipantsAsync(owner, id))["anna@acme.at"]);
    }

    [Fact]
    public async Task List_PagesWithTheTotal_SortedByNameOrStatus()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        factory.Emails.FailFor.Add("ben@acme.at");
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("clara@acme.at", "Clara Wolf"), Person("anna@acme.at", "Anna Berger"), Person("ben@acme.at", "Ben Huber") }, send = true }, Cancel);
        await RunJobsAsync(factory);

        var first = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?sort=name&limit=2"), Cancel);
        var second = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?sort=name&limit=2&offset=2"), Cancel);
        var byStatus = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?sort=-status"), Cancel);

        Assert.Equal(["anna@acme.at", "ben@acme.at"], Statuses(first).Keys);
        Assert.Equal(["clara@acme.at"], Statuses(second).Keys);
        Assert.Equal((3, 3), (first.GetProperty("total").GetInt32(), second.GetProperty("total").GetInt32()));
        Assert.Equal(new Dictionary<string, int> { ["sent"] = 2, ["bounced"] = 1 }, second.GetProperty("counts").Deserialize<Dictionary<string, int>>());
        Assert.Equal("bounced", byStatus.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Remind_TheChosen_RemindsOnlyThem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at"), Person("ben@acme.at"), Person("clara@acme.at") }, reminder = false, send = true }, Cancel);
        await RunJobsAsync(factory);
        var ids = Ids(await ParticipantsAsync(owner, id));
        factory.Emails.Sent.Clear();

        var reminded = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/participants/remind"), new { participantIds = new[] { ids["anna@acme.at"], ids["clara@acme.at"] } }, Cancel);
        await RunJobsAsync(factory);

        Assert.Equal(HttpStatusCode.Accepted, reminded.StatusCode);
        Assert.Equal(["anna@acme.at", "clara@acme.at"], factory.Emails.Sent.Select(sent => sent.Message.To).Order());
    }

    [Fact]
    public async Task Remove_TheChosen_KeepsThoseWhoResponded()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at"), Person("ben@acme.at"), Person("clara@acme.at") }, send = true }, Cancel);
        await RunJobsAsync(factory);
        var token = TokenOf(factory.Emails.Sent.Single(sent => sent.Message.To == "anna@acme.at").Message.Body);
        using var respondent = await factory.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "de", token }, Cancel)).EnsureSuccessStatusCode();
        var ids = Ids(await ParticipantsAsync(owner, id));

        var removed = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/participants/remove"), new { participantIds = new[] { ids["anna@acme.at"], ids["ben@acme.at"] } }, Cancel));
        var afterChosen = Statuses(await ParticipantsAsync(owner, id)).Keys.Order().ToList();
        var removedAll = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/participants/remove"), new { all = true }, Cancel));

        Assert.Equal((1, 1), (removed.GetProperty("removed").GetInt32(), removed.GetProperty("kept").GetInt32()));
        Assert.Equal(["anna@acme.at", "clara@acme.at"], afterChosen);
        Assert.Equal((1, 1), (removedAll.GetProperty("removed").GetInt32(), removedAll.GetProperty("kept").GetInt32()));
        Assert.Equal(["anna@acme.at"], Statuses(await ParticipantsAsync(owner, id)).Keys);
        Assert.Equal(2L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'participants.removed'"));
    }

    [Fact]
    public async Task Viewers_DoNotRemove()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at") }, send = false }, Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");

        var removed = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/participants/remove"), new { all = true }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, removed.StatusCode);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM participant"));
    }

    [Fact]
    public async Task Viewers_DoNotInvite()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");

        var invited = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("anna@acme.at") }, send = true }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, invited.StatusCode);
    }

    /// <summary>A minimal Excel workbook with shared strings, as Excel writes them.</summary>
    private static byte[] Workbook(params string[][] rows)
    {
        var strings = rows.SelectMany(row => row).Distinct().ToList();
        using var output = new MemoryStream();

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string name, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(xml);
            }

            const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Part("xl/workbook.xml", $"""<workbook xmlns="{Main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Liste" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Part("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Part("xl/sharedStrings.xml", $"""<sst xmlns="{Main}">{string.Concat(strings.Select(text => $"<si><t>{text}</t></si>"))}</sst>""");
            Part("xl/worksheets/sheet1.xml", $"""<worksheet xmlns="{Main}"><sheetData>{string.Concat(rows.Select((row, r) => $"<row r=\"{r + 1}\">{string.Concat(row.Select((cell, c) => $"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"s\"><v>{strings.IndexOf(cell)}</v></c>"))}</row>"))}</sheetData></worksheet>""");
        }

        return output.ToArray();
    }
}
