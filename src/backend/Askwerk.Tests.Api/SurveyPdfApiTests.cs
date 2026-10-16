// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Askwerk.Infrastructure.Pdf;
using Askwerk.Tests.Integration;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Askwerk.Tests.Api;

/// <summary>A survey as PDF to print (AWK-17 step 7, W10 E): our own renderer with PDFsharp/MigraDoc 6.2.4, styled from the theme.</summary>
public sealed class SurveyPdfApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public void PdfLibrary_IsPinnedToTheLastMitVersion()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (!File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent!;
        }

        var packages = File.ReadAllText(Path.Combine(root.FullName, "src", "Directory.Packages.props"));

        Assert.Contains("<PackageVersion Include=\"PDFsharp-MigraDoc\" Version=\"[6.2.4]\" />", packages, StringComparison.Ordinal);
        Assert.Equal(new Version(6, 2, 4), typeof(PdfDocument).Assembly.GetName().Version is { } version ? new Version(version.Major, version.Minor, version.Build) : null);
    }

    [Fact]
    public void Fonts_TheWoffFilesBecomeFontsThePdfCanEmbed()
    {
        var font = PdfFonts.Face("IBM Plex Sans", bold: false);

        Assert.Equal([0x00, 0x01, 0x00, 0x00], font[..4]);
        Assert.True(font.Length > 30_000);
    }

    [Fact]
    public async Task Pdf_OfASurvey_IsPrintable_InTheChosenLanguage()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var definition = RunnerApiTests.Definition();
        definition["pages"]![0]!["questions"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "q9", ["variable"] = "ASPECTS", ["type"] = "matrix", ["text"] = new JsonObject { ["de"] = "Wie bewerten Sie?", ["en"] = "How do you rate?" }, ["required"] = false,
            ["rows"] = new JsonArray(new JsonObject { ["code"] = "a", ["label"] = new JsonObject { ["de"] = "Preis", ["en"] = "Price" } }),
            ["options"] = new JsonArray(new JsonObject { ["code"] = "1", ["label"] = new JsonObject { ["de"] = "Gut", ["en"] = "Good" } }, new JsonObject { ["code"] = "2", ["label"] = new JsonObject { ["de"] = "Schlecht", ["en"] = "Poor" } }),
        });
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{surveyId}/draft"), new { definition }, Cancel)).EnsureSuccessStatusCode();

        var answer = await owner.GetAsync(Url($"/api/app/surveys/{surveyId}/pdf?language=en"), Cancel);
        var bytes = await answer.Content.ReadAsByteArrayAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("application/pdf", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(("Customer satisfaction", "Acme GmbH"), (pdf.Info.Title, pdf.Info.Author));
        Assert.True(pdf.PageCount >= 1);
    }

    [Fact]
    public async Task Pdf_IsForTheOrganisationOnly()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await SurveysApiTests.CreateSurveyAsync(owner);
        using var stranger = await factory.CreateReadyClientAsync();

        var anonymous = await stranger.GetAsync(Url($"/api/app/surveys/{surveyId}/pdf"), Cancel);
        var unknown = await owner.GetAsync(Url($"/api/app/surveys/{Guid.NewGuid()}/pdf"), Cancel);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.NotFound), (anonymous.StatusCode, unknown.StatusCode));
    }
}
