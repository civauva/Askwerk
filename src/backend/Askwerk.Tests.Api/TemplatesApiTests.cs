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
}
