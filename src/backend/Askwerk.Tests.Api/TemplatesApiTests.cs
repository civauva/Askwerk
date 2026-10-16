// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Templates;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Survey templates (AWK-17 step 5, W10 C): Askwerk's own and the organisation's, and new surveys from them.</summary>
public sealed class TemplatesApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static async Task<JsonElement[]> TemplatesAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Url("/api/app/templates"), Cancel)).GetProperty("items").EnumerateArray()];

    [Fact]
    public void AskwerksTemplates_PassThePublishChecksWithoutAnyWarning()
    {
        Assert.All(SystemTemplates.All, template => Assert.Empty(DefinitionChecks.Check(template.Definition, previous: null).Select(issue => $"{template.Id} {issue.Code} {issue.Path}")));
    }

    [Fact]
    public async Task List_OffersAskwerksTemplates_ThenTheOrganisationsOwn()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();

        var before = await TemplatesAsync(owner);
        var saved = await owner.PostAsJsonAsync(Url("/api/app/templates"), new { surveyId, name = "Acme check-in" }, Cancel);
        var after = await TemplatesAsync(owner);

        Assert.Equal(["askwerk:customer-satisfaction", "askwerk:employee-pulse", "askwerk:event-feedback", "askwerk:nps-short"], before.Select(item => item.GetProperty("id").GetString()));
        Assert.All(before, item => Assert.Equal("askwerk", item.GetProperty("scope").GetString()));
        Assert.Equal("Kundenzufriedenheit", before[0].GetProperty("names").GetProperty("de").GetString());
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var own = after.Last();
        Assert.Equal(("organisation", "Acme check-in", 4), (own.GetProperty("scope").GetString(), own.GetProperty("name").GetString(), own.GetProperty("questions").GetInt32()));
    }

    [Fact]
    public async Task Create_FromATemplate_CopiesItsDefinitionAndLanguages()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var template = (await TemplatesAsync(owner)).Single(item => item.GetProperty("id").GetString() == "askwerk:nps-short");

        var created = await owner.PostAsJsonAsync(Url("/api/app/surveys"), new { title = "NPS Q4", templateId = "askwerk:nps-short" }, Cancel);
        var id = (await JsonAsync(created)).GetProperty("id").GetString();
        var draft = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/draft"), Cancel);
        var definition = draft.GetProperty("definition");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("NPS Q4", draft.GetProperty("title").GetString());
        Assert.Equal(template.GetProperty("questions").GetInt32(), definition.GetProperty("pages").EnumerateArray().Sum(page => page.GetProperty("questions").GetArrayLength()));
        Assert.Equal(["de", "en"], definition.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
    }

    [Fact]
    public async Task Create_FromAnUnknownTemplate_IsRefusedAtTheField()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var created = await owner.PostAsJsonAsync(Url("/api/app/surveys"), new { title = "X", templateId = "askwerk:nothing" }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal("template-not-found", (await JsonAsync(created)).GetProperty("errors").GetProperty("templateId")[0].GetString());
    }

    [Fact]
    public async Task OwnTemplates_AreDeleted_AskwerksStay_AndViewersCannotSave()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        var saved = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/templates"), new { surveyId, name = "Mine" }, Cancel));

        var deleteOwn = await owner.DeleteAsync(Url($"/api/app/templates/{saved.GetProperty("id").GetString()}"), Cancel);
        var deleteAskwerks = await owner.DeleteAsync(Url("/api/app/templates/askwerk:nps-short"), Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");
        var viewerSaves = await owner.PostAsJsonAsync(Url("/api/app/templates"), new { surveyId, name = "Not allowed" }, Cancel);

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.Forbidden], [deleteOwn.StatusCode, deleteAskwerks.StatusCode, viewerSaves.StatusCode]);
        Assert.Equal(4, (await TemplatesAsync(owner)).Length);
    }

    [Fact]
    public async Task Edit_OpensASurveyOfItsOwn_NeverListedOrPublished_AndNewSurveysTakeItsChangesAndLook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        var templateId = (await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/templates"), new { surveyId, name = "Acme check-in" }, Cancel))).GetProperty("id").GetString();

        var editResponse = await owner.PostAsync(Url($"/api/app/templates/{templateId}/edit"), null, Cancel);
        Assert.True(editResponse.IsSuccessStatusCode, $"{editResponse.StatusCode} {await editResponse.Content.ReadAsStringAsync(Cancel)}");
        var edit = await JsonAsync(editResponse);
        var editing = edit.GetProperty("surveyId").GetString();
        var again = await JsonAsync(await owner.PostAsync(Url($"/api/app/templates/{templateId}/edit"), null, Cancel));
        var changed = RunnerApiTests.Definition();
        changed["welcome"]!["title"]!["en"] = "Acme check-in, edited";
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{editing}/draft"), new { definition = changed }, Cancel)).EnsureSuccessStatusCode();
        var look = ThemesApiTests.Werkstatt();
        look["colours"]!["accent"] = "#1f5f8b";
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{editing}/look"), new { tokens = look, lookBase = "preset:werkstatt" }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{editing}/title"), new { title = "Acme visit" }, Cancel)).EnsureSuccessStatusCode();
        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{editing}/summary"), Cancel);
        var publish = await owner.PostAsync(Url($"/api/app/surveys/{editing}/publish"), null, Cancel);
        var own = (await TemplatesAsync(owner)).Last();
        var created = (await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/surveys"), new { title = "From it", templateId }, Cancel))).GetProperty("id").GetString();
        var draft = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{created}/draft"), Cancel);
        var createdLook = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{created}/look"), Cancel);

        Assert.Equal(editing, again.GetProperty("surveyId").GetString());
        Assert.Equal([surveyId.ToString()], list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(1, list.GetProperty("counts").GetProperty("all").GetInt32());
        Assert.Equal(templateId, summary.GetProperty("templateId").GetString());
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);
        Assert.Equal("template-not-publishable", (await JsonAsync(publish)).GetProperty("code").GetString());
        Assert.Equal(("Acme visit", editing), (own.GetProperty("name").GetString(), own.GetProperty("surveyId").GetString()));
        Assert.Equal("Acme check-in, edited", own.GetProperty("front").GetProperty("title").GetProperty("en").GetString());
        Assert.Equal("#1f5f8b", own.GetProperty("front").GetProperty("look").GetProperty("colours").GetProperty("accent").GetString());
        Assert.Equal("Acme check-in, edited", draft.GetProperty("definition").GetProperty("welcome").GetProperty("title").GetProperty("en").GetString());
        Assert.True(createdLook.GetProperty("own").GetBoolean());
        Assert.Equal("#1f5f8b", createdLook.GetProperty("tokens").GetProperty("colours").GetProperty("accent").GetString());
    }

    [Fact]
    public async Task Copy_MakesAskwerksTemplateOurs_ReadyToEdit_AndDeletingItTakesItsSurvey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var copied = await owner.PostAsJsonAsync(Url("/api/app/templates/askwerk:nps-short/copy"), new { language = "en" }, Cancel);
        var copy = await JsonAsync(copied);
        var own = (await TemplatesAsync(owner)).Last();
        var deleted = await owner.DeleteAsync(Url($"/api/app/templates/{copy.GetProperty("id").GetString()}"), Cancel);
        var summary = await owner.GetAsync(Url($"/api/app/surveys/{copy.GetProperty("surveyId").GetString()}/summary"), Cancel);

        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        Assert.Equal(("organisation", copy.GetProperty("surveyId").GetString()), (own.GetProperty("scope").GetString(), own.GetProperty("surveyId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(own.GetProperty("name").GetString()));
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], [deleted.StatusCode, summary.StatusCode]);
        Assert.Equal(4, (await TemplatesAsync(owner)).Length);
    }

    [Fact]
    public async Task Cards_DrawEachSurveysWelcomePageInItsLook()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();

        var plain = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);
        var cards = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys?cards=true"), Cancel);
        var front = cards.GetProperty("items")[0].GetProperty("front");

        Assert.Equal(JsonValueKind.Null, plain.GetProperty("items")[0].GetProperty("front").ValueKind);
        Assert.Equal(("Customer satisfaction", "Thank you!", "de"), (front.GetProperty("title").GetProperty("en").GetString(), front.GetProperty("text").GetProperty("en").GetString(), front.GetProperty("defaultLanguage").GetString()));
        Assert.True(front.GetProperty("look").GetProperty("colours").TryGetProperty("accent", out _));
        Assert.All(await TemplatesAsync(owner), template => Assert.Equal(JsonValueKind.Object, template.GetProperty("front").GetProperty("look").ValueKind));
    }
}
