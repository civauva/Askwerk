// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Samples;
using Askwerk.Infrastructure.Seeding;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>The demo survey (AWK-23): every organisation has one published survey that shows every question type.</summary>
public sealed class DemoSurveyApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement[]> SurveysAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel)).GetProperty("items").EnumerateArray()];

    [Fact]
    public void Definition_UsesEveryQuestionType_AndPassesThePublishChecksWithoutAnyWarning()
    {
        var definition = DemoSurvey.Definition();

        Assert.Equal(Enum.GetValues<QuestionType>().ToHashSet(), definition.Pages.SelectMany(page => page.Questions).Select(question => question.Type).ToHashSet());
        Assert.Equal(["de", "en"], definition.Languages);
        Assert.Empty(DefinitionChecks.Check(definition, previous: null).Select(issue => $"{issue.Code} {issue.Path}"));
    }

    [Fact]
    public async Task AfterSetup_TheOrganisationHasTheDemoSurvey_PublishedAndOpenToRespondents()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        factory.DemoSurvey = true;
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var surveys = await SurveysAsync(owner);
        var demo = surveys.Single(survey => survey.GetProperty("title").GetString() == DemoSurvey.Title);
        using var respondent = await factory.CreateReadyClientAsync();
        var opened = await respondent.GetAsync(Url($"/api/app/run/{demo.GetProperty("id").GetString()}"), Cancel);

        Assert.Equal((DemoSurvey.Title, "published"), (demo.GetProperty("title").GetString(), demo.GetProperty("status").GetString()));
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var definition = (await opened.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("survey").GetProperty("definition");
        var picture = definition.GetProperty("pages")[0].GetProperty("questions")[1].GetProperty("image").GetProperty("asset").GetString();
        var file = await respondent.GetAsync(Url($"/api/app/run/{demo.GetProperty("id").GetString()}/assets/{picture}?w=480"), Cancel);
        Assert.Equal(("image/png", (480, 180)), (file.Content.Headers.ContentType?.MediaType, AssetImagesApiTests.PngSize(await file.Content.ReadAsByteArrayAsync(Cancel))));
    }

    [Fact]
    public async Task Restarting_KeepsOneDemoSurvey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        factory.DemoSurvey = true;
        using (var owner = await SurveysApiTests.SignedInOwnerAsync(factory))
        {
            Assert.Equal(7, (await SurveysAsync(owner)).Length);
        }

        await using var restarted = new AskwerkApiFactory(factory.OwnerConnectionString, Path.Combine(Path.GetTempPath(), "askwerk-test-" + Guid.NewGuid().ToString("N"))) { DemoSurvey = true };
        await restarted.CreateReadyClientAsync();

        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, $"SELECT count(*) FROM survey WHERE title = '{DemoSurvey.Title}'"));
        Assert.Equal(7L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM survey"));
    }

    [Fact]
    public void Demos_AreSix_Rich_AndPassThePublishChecks()
    {
        Assert.Equal(["museum", "travel", "patients", "course", "banking", "suppliers"], SampleSurveys.Demos.Select(demo => demo.Id));

        foreach (var demo in SampleSurveys.Demos)
        {
            var pictures = SampleSurveys.PictureNames(demo);
            var definition = SampleSurveys.DefinitionWith(demo, pictures.ToDictionary(name => name, _ => Guid.NewGuid()));
            var questions = definition.Pages.SelectMany(page => page.Questions).ToList();

            Assert.DoesNotContain(DefinitionChecks.Check(definition), issue => issue.Severity == IssueSeverity.Error);
            Assert.True(definition.Pages.Count >= 3, demo.Id);
            Assert.True(questions.Count >= 10, demo.Id);
            Assert.Contains(questions, question => question.ShowWhen is not null);
            Assert.Contains(questions, question => question.Type is QuestionType.Matrix or QuestionType.Ranking or QuestionType.Slider);
            Assert.StartsWith("Demo: ", demo.Title!["en"], StringComparison.Ordinal);
            // One or two key questions (W38), so their cards show a real key result.
            Assert.InRange(questions.Count(question => question.Key == true), 1, 2);
            Assert.All(pictures, name => Assert.Equal([0x89, 0x50, 0x4e, 0x47], SampleSurveys.Picture(name).Png[..4]));
        }

        // The two graphics-rich ones show pictures; the industry ones need none.
        Assert.True(SampleSurveys.PictureNames(SampleSurveys.Demos[0]).Count >= 5);
        Assert.True(SampleSurveys.PictureNames(SampleSurveys.Demos[1]).Count >= 7);
    }

    [Fact]
    public async Task AfterSetup_TheSixDemosArePublished_WithTheirLooksAndSampleAnswers()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        factory.DemoSurvey = true;
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var surveys = await SurveysAsync(owner);
        var museum = surveys.Single(survey => survey.GetProperty("title").GetString() == "Demo: City museum visit");
        var id = museum.GetProperty("id").GetString();
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);
        var look = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/look"), Cancel);

        Assert.Equal(7, surveys.Length);
        Assert.All(surveys, survey => Assert.Equal("published", survey.GetProperty("status").GetString()));
        Assert.Equal(180, summary.GetProperty("responses").GetInt32());
        Assert.Equal("preset:atelier", look.GetProperty("lookBase").GetString());
    }

    [Fact]
    public async Task SwitchedOff_NoDemoSurvey()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        Assert.Empty(await SurveysAsync(owner));
    }
}
