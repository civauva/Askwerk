// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// Participants, rich (W35): a list read as a table and its columns mapped, people matched by External ID before the
/// email, fields as placeholders, one person and their history, filters, export, automatic reminders, and Contacts.
/// </summary>
public sealed class ParticipantsRichApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    private static async Task<(HttpClient Owner, string SurveyId)> PublishedAsync(AskwerkApiFactory factory)
    {
        var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        await SurveysApiTests.ScalarAsync(factory, "UPDATE instance_settings SET smtp_host = 'smtp.acme.at', smtp_port = 587, smtp_sender = 'Acme Surveys <surveys@acme.at>'");

        return (owner, id);
    }

    private static object Person(string email, string name, string externalId, string department) =>
        new { email, name, language = "de", externalId, fields = new Dictionary<string, string> { ["Department"] = department } };

    [Fact]
    public async Task Table_ReadsAPastedSheet_AndGuessesItsColumns()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;

        var table = await JsonAsync(await owner.PostAsJsonAsync(
            Url($"/api/app/surveys/{id}/participants/table"),
            new { text = "E-Mail-Adresse\tVorname\tNachname\tSprache\tKundennummer\tAbteilung\nlena@acme.at\tLena\tGruber\tde\t10442\tSales\nthomas@acme.at\tThomas\tHuber\tde\t10871\tService\n" },
            Cancel));

        Assert.Equal(["E-Mail-Adresse", "Vorname", "Nachname", "Sprache", "Kundennummer", "Abteilung"], table.GetProperty("columns").EnumerateArray().Select(column => column.GetString()));
        Assert.Equal(["email", "firstName", "lastName", "language", "externalId", "field"], table.GetProperty("guesses").EnumerateArray().Select(guess => guess.GetString()));
        Assert.Equal(2, table.GetProperty("rows").GetArrayLength());
        Assert.Equal("10871", table.GetProperty("rows")[1][4].GetString());
    }

    [Fact]
    public async Task Invite_WithExternalIdsAndFields_MatchesByExternalIdFirst_FiltersExportsAndSavesToContacts()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;

        var invited = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new
        {
            participants = new[] { Person("lena@acme.at", "Lena Gruber", "CRM-1", "Sales"), Person("thomas@acme.at", "Thomas Huber", "CRM-2", "Service") },
            saveToContacts = true,
            contactGroup = "Customers Q4",
        }, Cancel);
        // The same External ID with a new address counts as already invited.
        var check = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/participants/check-people"), new { people = new[] { Person("lena.new@acme.at", "Lena", "CRM-1", "Sales"), Person("sarah@acme.at", "Sarah", "CRM-3", "Sales") } }, Cancel));
        var sales = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?field=department:Sales"), Cancel);
        var search = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?search=CRM-2"), Cancel);
        var csv = await owner.GetStringAsync(Url($"/api/app/surveys/{id}/participants.csv"), Cancel);
        var contacts = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/contacts"), Cancel);

        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        Assert.Equal((1, "sarah@acme.at"), (check.GetProperty("alreadyInvited").GetInt32(), check.GetProperty("participants")[0].GetProperty("email").GetString()));
        Assert.Equal(["lena@acme.at"], sales.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        Assert.Equal(("CRM-1", "Sales"), (sales.GetProperty("items")[0].GetProperty("externalId").GetString(), sales.GetProperty("items")[0].GetProperty("fields").GetProperty("department").GetString()));
        Assert.Equal(["department"], sales.GetProperty("fieldKeys").EnumerateArray().Select(key => key.GetString()));
        Assert.Equal(["thomas@acme.at"], search.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        Assert.StartsWith("email,name,language,external_id,status,sent_at,last_activity,reminders,link,department", csv.TrimStart((char)0xFEFF), StringComparison.Ordinal);
        Assert.Contains("lena@acme.at,Lena Gruber,de,CRM-1,waiting", csv, StringComparison.Ordinal);
        Assert.Equal(2, contacts.GetProperty("matching").GetInt32());
        Assert.Equal(("Customers Q4", 2), (contacts.GetProperty("groups")[0].GetProperty("name").GetString(), contacts.GetProperty("groups")[0].GetProperty("count").GetInt32()));

        // The invitation fills the field placeholders.
        (await owner.PutAsJsonAsync(Url("/api/app/messages/invitation"), new { subject = new { de = "Für {department}", en = "For {department}" }, body = new { de = "Hallo {name} ({external_id}): {link}", en = "Hi {name}: {link}" } }, Cancel)).EnsureSuccessStatusCode();
        await RunJobsAsync(factory);
        var lena = factory.Emails.Sent.Single(sent => sent.Message.To == "lena@acme.at").Message;
        Assert.Equal("Für Sales", lena.Subject);
        Assert.Contains("Hallo Lena Gruber (CRM-1)", lena.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnePerson_ShowsTheHistory_IsChanged_AndGetsNoMoreReminders()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        (await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("lena@acme.at", "Lena Gruber", "CRM-1", "Sales"), Person("thomas@acme.at", "Thomas", "CRM-2", "Sales") } }, Cancel)).EnsureSuccessStatusCode();
        await RunJobsAsync(factory);
        var items = (await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?sort=name"), Cancel)).GetProperty("items");
        var lenaId = items.EnumerateArray().Single(item => item.GetProperty("email").GetString() == "lena@acme.at").GetProperty("id").GetString();

        var taken = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/participants/{lenaId}"), Person("thomas@acme.at", "Lena", "CRM-1", "Sales"), Cancel);
        var changed = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/participants/{lenaId}"), Person("lena.gruber@acme.at", "Lena Gruber", "CRM-1", "Marketing"), Cancel);
        var off = await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/participants/{lenaId}/reminders"), new { off = true }, Cancel);
        var detail = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants/{lenaId}"), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal("email-taken", (await JsonAsync(taken)).GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent], [changed.StatusCode, off.StatusCode]);
        Assert.Equal(("lena.gruber@acme.at", "Marketing", true), (detail.GetProperty("email").GetString(), detail.GetProperty("fields").GetProperty("department").GetString(), detail.GetProperty("remindersOff").GetBoolean()));
        Assert.Equal(["invited", "sent"], detail.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()));
        Assert.Contains("?t=", detail.GetProperty("link").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutomaticReminders_GoToThoseDueWhoHaveNotAnswered_UpToTheMost()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;
        (await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { participants = new[] { Person("lena@acme.at", "Lena", "CRM-1", "Sales"), Person("thomas@acme.at", "Thomas", "CRM-2", "Sales"), Person("sarah@acme.at", "Sarah", "CRM-3", "Sales") } }, Cancel)).EnsureSuccessStatusCode();
        await RunJobsAsync(factory);
        var ids = (await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants"), Cancel)).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("email").GetString()!, item => item.GetProperty("id").GetString()!);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/participants/{ids["sarah@acme.at"]}/reminders"), new { off = true }, Cancel)).EnsureSuccessStatusCode();
        // Lena and Sarah were invited ten days ago; Thomas today.
        await SurveysApiTests.ScalarAsync(factory, $"UPDATE participant SET sent_at = now() - interval '10 days' WHERE email IN ('lena@acme.at', 'sarah@acme.at')");
        factory.Emails.Sent.Clear();

        var set = await JsonAsync(await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/reminders"), new { on = true, days = 7, max = 1 }, Cancel));
        await RunJobsAsync(factory);
        await RunJobsAsync(factory);
        var list = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants"), Cancel);

        Assert.Equal((true, 7, 1, 1), (set.GetProperty("on").GetBoolean(), set.GetProperty("days").GetInt32(), set.GetProperty("max").GetInt32(), set.GetProperty("nextCount").GetInt32()));
        Assert.Equal(["lena@acme.at"], factory.Emails.Sent.Select(sent => sent.Message.To));
        // At most one: Lena is done; Thomas is due in a week.
        Assert.Equal(1, list.GetProperty("autoReminders").GetProperty("nextCount").GetInt32());
        Assert.True(list.GetProperty("autoReminders").GetProperty("nextAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Contacts_AreImportedByExternalId_Grouped_Exported_AndInvited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await PublishedAsync(factory);
        using var _ = owner;

        var first = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/contacts/import"), new { people = new[] { Person("lena@acme.at", "Lena", "CRM-1", "Sales"), Person("thomas@acme.at", "Thomas", "CRM-2", "Service"), new { email = "nope", name = "x", language = "de", externalId = "", fields = new Dictionary<string, string>() } }, group = "Key accounts" }, Cancel));
        // Lena's address changed in the CRM: the External ID keeps her the same contact.
        var again = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/contacts/import"), new { people = new[] { Person("lena.gruber@acme.at", "Lena Gruber", "CRM-1", "Sales") } }, Cancel));
        var contacts = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/contacts?group=Key%20accounts"), Cancel);
        var contactIds = contacts.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        var duplicate = await owner.PostAsJsonAsync(Url("/api/app/contacts"), new { email = "new@acme.at", externalId = "CRM-2" }, Cancel);
        var csv = await owner.GetStringAsync(Url("/api/app/contacts/contacts.csv"), Cancel);
        var invited = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/invitations"), new { contactIds, send = false }, Cancel);
        var participants = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/participants?sort=name"), Cancel);
        var linked = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/contacts"), Cancel);

        Assert.Equal((2, 0, 1), (first.GetProperty("added").GetInt32(), first.GetProperty("updated").GetInt32(), first.GetProperty("invalid").GetArrayLength()));
        Assert.Equal((0, 1), (again.GetProperty("added").GetInt32(), again.GetProperty("updated").GetInt32()));
        Assert.Equal(["lena.gruber@acme.at", "thomas@acme.at"], contacts.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()));
        Assert.Equal("external-id-taken", (await JsonAsync(duplicate)).GetProperty("errors").GetProperty("externalId")[0].GetString());
        Assert.Contains("lena.gruber@acme.at,Lena Gruber,de,CRM-1,Key accounts,Sales", csv, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        Assert.Equal(["CRM-1", "CRM-2"], participants.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("externalId").GetString()).Order());
        Assert.All(linked.GetProperty("items").EnumerateArray(), item => Assert.Equal(1, item.GetProperty("surveys").GetInt32()));
    }
}
