// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Api.App.Messages;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Pdf;
using Askwerk.Infrastructure.Samples;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Sample surveys (W26): each with its own look, its pictures and sample answers, closed until reopened.</summary>
public sealed class SamplesApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public void Samples_PassThePublishChecks_AndTheirLooksPassTheThemeChecks()
    {
        Assert.Equal(["cafe", "pulse", "festival", "board", "inspection", "kiosk", "employees", "night", "lodge"], SampleSurveys.All.Select(sample => sample.Id));
        Assert.Equal(17, SampleSurveys.Looks.Count);

        foreach (var sample in SampleSurveys.All)
        {
            var assets = SampleSurveys.PictureNames(sample).ToDictionary(name => name, _ => Guid.NewGuid());
            var definition = SampleSurveys.DefinitionWith(sample, assets);

            Assert.DoesNotContain(DefinitionChecks.Check(definition), issue => issue.Severity == IssueSeverity.Error);
            Assert.Empty(ThemeChecks.Check(SampleSurveys.Looks[sample.Look]));
        }
    }

    [Fact]
    public void Answers_FollowTheRules_AndAreTheSameForTheSameSeed()
    {
        var sample = SampleSurveys.Find("inspection")!;
        var definition = SampleSurveys.DefinitionWith(sample, new Dictionary<string, Guid>());
        var first = Enumerable.Range(0, 40).Select(_ => SampleSurveys.Answers(sample, definition, new Random(7))).First();
        var again = SampleSurveys.Answers(sample, definition, new Random(7));
        var all = Enumerable.Range(0, 60).Select(seed => SampleSurveys.Answers(sample, definition, new Random(seed)).Answers).ToList();

        Assert.Equal(first.Answers.Keys, again.Answers.Keys);
        // The follow-up shows only when follow-up is needed (yes = "1").
        Assert.All(all, answers => Assert.Equal(answers["needed"].Option == "1", answers.ContainsKey("followup")));
        // Every page the respondent sees would pass the server's checks.
        Assert.All(all, answers => Assert.All(
            SurveyLogic.Route(definition, answers),
            routed => Assert.Empty(AnswerRules.CheckPage(routed.Page with { Questions = routed.Questions }, answers, DateOnly.FromDateTime(DateTime.UtcNow)))));
    }

    [Fact]
    public void EveryLook_PrintsEverySample_AsPdf()
    {
        foreach (var sample in SampleSurveys.All)
        {
            var definition = SampleSurveys.DefinitionWith(sample, new Dictionary<string, Guid>());
            var pdf = SurveyPdf.Render(definition, SampleSurveys.Looks[sample.Look], new SurveyPdfContent(sample.Names["en"], "Acme GmbH", "https://surveys.acme.at/s/abc", "en", new Dictionary<string, byte[]>()));

            Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        }
    }

    [Fact]
    public void Pdf_PrintsTheMountainsAsABand_AndIsLargerThanWithoutIt()
    {
        var sample = SampleSurveys.Find("lodge")!;
        var definition = SampleSurveys.DefinitionWith(sample, new Dictionary<string, Guid>());
        var content = new SurveyPdfContent("Mountain lodge", "Lodge Falkenstein", "https://surveys.acme.at/s/abc", "en", new Dictionary<string, byte[]>());
        var panorama = SampleSurveys.Looks["panorama"];

        var withBand = SurveyPdf.Render(definition, panorama, content);
        var plain = SurveyPdf.Render(definition, panorama with { Pdf = new ThemePdf(PdfHeader.Plain) }, content);

        Assert.Equal([0x89, 0x50, 0x4e, 0x47], PdfScenes.Mountains()[..4]);
        Assert.True(withBand.Length > plain.Length + 10_000);
    }

    [Fact]
    public void Pdf_PrintsThePicturesOfPictureChoices()
    {
        var sample = SampleSurveys.Find("cafe")!;
        var assets = SampleSurveys.PictureNames(sample).ToDictionary(name => name, _ => Guid.NewGuid());
        var definition = SampleSurveys.DefinitionWith(sample, assets);
        var cakes = new[] { "strudel", "sacher", "cheesecake", "croissant" };
        var pictures = cakes.ToDictionary(name => assets[name].ToString(), name => SampleSurveys.Picture(name).Png);


        var with = SurveyPdf.Render(definition, SampleSurveys.Looks["papier"], PdfContentFor(pictures));
        var without = SurveyPdf.Render(definition, SampleSurveys.Looks["papier"], PdfContentFor(new Dictionary<string, byte[]>()));

        Assert.True(with.Length > without.Length + 20_000);

        static SurveyPdfContent PdfContentFor(Dictionary<string, byte[]> images) => new("Café visit", "Café Falke", "https://surveys.acme.at/s/abc", "en", images);
    }

    [Fact]
    public void Email_FollowsTheLooksHeader_LogoBandOrPlain()
    {
        EmailContent Content() => new("Your opinion", "Please answer.\n\nhttps://surveys.acme.at/s/abc", "https://surveys.acme.at/s/abc", "Start", "Acme GmbH", "https://acme.at/logo.png", null, "", null);
        var konfetti = SampleSurveys.Looks["konfetti"];
        var papier = SampleSurveys.Looks["papier"];

        var (band, _) = EmailRenderer.Render(konfetti, Content(), "en");
        var (plain, _) = EmailRenderer.Render(papier, Content(), "en");
        var (logo, _) = EmailRenderer.Render(SampleSurveys.Looks["werkstatt"], Content(), "en");

        Assert.Contains($"background:{konfetti.Colours.Accent};color:{konfetti.Colours.OnAccent}", band, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logo.png", plain, StringComparison.Ordinal);
        Assert.Contains("Acme GmbH", plain, StringComparison.Ordinal);
        Assert.Contains("logo.png", logo, StringComparison.Ordinal);
    }

    [Fact]
    public void Pictures_AreDrawn()
    {
        foreach (var name in new[] { "cafe", "garden", "strudel", "sacher", "cheesecake", "croissant", "coffee", "tea", "lemonade", "team" })
        {
            var picture = SampleSurveys.Picture(name);
            Assert.Equal([0x89, 0x50, 0x4e, 0x47], picture.Png[..4]);
        }
    }

    [Fact]
    public async Task Create_MakesAClosedSurveyWithItsLook_PicturesAndSampleAnswers()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var list = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/samples"), Cancel);
        var created = await owner.PostAsJsonAsync(Url("/api/app/samples/cafe"), new { title = "Café visit" }, Cancel);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetString();
        var summary = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/summary"), Cancel);
        var look = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/look"), Cancel);
        var draft = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/draft"), Cancel);
        var results = await owner.GetAsync(Url($"/api/app/surveys/{id}/results"), Cancel);
        var unknown = await owner.PostAsJsonAsync(Url("/api/app/samples/nothing"), new { title = "x" }, Cancel);

        var cafe = list.GetProperty("items")[0];
        Assert.Equal(("cafe", "atelier", 9), (cafe.GetProperty("id").GetString(), cafe.GetProperty("look").GetString(), list.GetProperty("items").GetArrayLength()));
        Assert.Equal("Which cake did you try?", cafe.GetProperty("preview").GetProperty("question").GetProperty("en").GetString());
        Assert.Equal("hills", cafe.GetProperty("tokens").GetProperty("decoration").GetProperty("pattern").GetString());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("closed", 86), (summary.GetProperty("status").GetString(), summary.GetProperty("responses").GetInt32()));
        Assert.Equal((true, "preset:atelier"), (look.GetProperty("own").GetBoolean(), look.GetProperty("lookBase").GetString()));
        var picture = draft.GetProperty("definition").GetProperty("pages")[0].GetProperty("questions")[1].GetProperty("options")[0].GetProperty("image").GetProperty("asset").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(Url($"/api/app/assets/{picture}"), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, results.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
