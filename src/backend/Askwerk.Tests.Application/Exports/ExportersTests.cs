// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Askwerk.Application.Exports;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Application.Exports;

/// <summary>
/// The result exports (AWK-24): one exporter per format, each checked against golden files in
/// golden/&lt;format&gt;/ (agents/coding-standards.md). Zip files are compared entry by entry, as text.
/// </summary>
public sealed class ExportersTests
{
    private const string Questions = """
        [
          {"id":"q1","variable":"SOURCE","type":"single","text":{"en":"Where from?"},"required":false,
           "options":[{"code":"1","label":{"en":"Search"}},{"code":"2","label":{"en":"Ads"}}],"other":{"code":"9","label":{"en":"Other:"},"variable":"SOURCE_OTHER"}},
          {"id":"q2","variable":"CH","type":"multiple","text":{"en":"Which, \"if any\"?"},"required":false,"options":[{"code":"a","label":{"en":"A"}},{"code":"b","label":{"en":"B"}}]},
          {"id":"q3","variable":"SAT","type":"rating","text":{"en":"Satisfied?"},"required":false,"scale":{"min":1,"max":5},"dontKnow":true},
          {"id":"q4","variable":"TXT","type":"long-text","text":{"en":"Anything else?"},"required":false}
        ]
        """;

    private static readonly DateTimeOffset Started = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private static ExportData Data()
    {
        var definition = JsonSerializer.Deserialize<SurveyDefinition>($$"""{"schemaVersion":1,"languages":["en"],"defaultLanguage":"en","pages":[{"id":"p1","questions":{{Questions}}}]}""", SurveyDefinitionJson.Options)!;

        return new ExportData(
            Guid.Parse("01a13b8f-0000-7000-8000-000000000001"),
            "Customer satisfaction 2026",
            "customer-satisfaction-2026",
            Codebook.Of(definition, "en"),
            new DateTimeOffset(2026, 10, 14, 18, 0, 0, TimeSpan.Zero),
            Responses());
    }

    private static async IAsyncEnumerable<ExportResponse> Responses([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ExportResponse(
            Guid.Parse("01a13b8f-0000-7000-8000-00000000000a"),
            "complete",
            1,
            "en",
            Started,
            Started.AddSeconds(190),
            new Dictionary<string, AnswerValue>
            {
                ["q1"] = new(Option: "9", Other: "Trade fair, \"Vienna\""),
                ["q2"] = new(Options: ["a", "b"]),
                ["q3"] = new(Number: 4m),
                ["q4"] = new(Text: "=SUM(A1)\nsecond line; Grüße"),
            });
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ExportResponse(
            Guid.Parse("01a13b8f-0000-7000-8000-00000000000b"),
            "partial",
            1,
            "de",
            Started.AddHours(1),
            null,
            new Dictionary<string, AnswerValue> { ["q1"] = new(Option: "2"), ["q3"] = new(DontKnow: true) });
    }

    private static async Task<byte[]> ExportAsync(IExporter exporter, ExportOptions options)
    {
        using var output = new MemoryStream();
        await exporter.WriteAsync(Data(), options, output, TestContext.Current.CancellationToken);

        return output.ToArray();
    }

    private static Dictionary<string, string> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    [Theory]
    [InlineData(ExportLayout.Wide, ExportValues.Labels, "wide-labels.csv")]
    [InlineData(ExportLayout.Wide, ExportValues.Codes, "wide-codes.csv")]
    [InlineData(ExportLayout.Wide, ExportValues.Both, "wide-both.csv")]
    [InlineData(ExportLayout.Long, ExportValues.Labels, "long-labels.csv")]
    public async Task Csv_MatchesTheGoldenFile(ExportLayout layout, ExportValues values, string golden)
    {
        var bytes = await ExportAsync(new CsvExporter(), new ExportOptions(layout, values));

        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
        Golden.AssertMatches($"csv/{golden}", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task Excel_IsAWorkbookWithResponsesAndVariables_MatchingTheGoldenFiles()
    {
        var entries = Entries(await ExportAsync(new XlsxExporter(), new ExportOptions(ExportLayout.Wide, ExportValues.Labels)));

        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml"],
            entries.Keys);
        Golden.AssertMatches("xlsx/sheet1.xml", entries["xl/worksheets/sheet1.xml"]);
        Golden.AssertMatches("xlsx/sheet2.xml", entries["xl/worksheets/sheet2.xml"]);
        Golden.AssertMatches("xlsx/workbook.xml", entries["xl/workbook.xml"]);
    }

    [Fact]
    public async Task Spss_IsCodesAsCsv_PlusSyntaxWithLabelsAndTypes()
    {
        var entries = Entries(await ExportAsync(new SpssExporter(), new ExportOptions(ExportLayout.Long, ExportValues.Labels)));

        Assert.Equal(["customer-satisfaction-2026.csv", "customer-satisfaction-2026.sps"], entries.Keys);
        Golden.AssertMatches("spss/customer-satisfaction-2026.csv", entries["customer-satisfaction-2026.csv"].TrimStart('﻿'));
        Golden.AssertMatches("spss/customer-satisfaction-2026.sps", entries["customer-satisfaction-2026.sps"]);
    }

    [Fact]
    public async Task Stata_IsCodesAsCsv_PlusADoFile()
    {
        var entries = Entries(await ExportAsync(new StataExporter(), new ExportOptions(ExportLayout.Wide, ExportValues.Labels)));

        Assert.Equal(["customer-satisfaction-2026.csv", "customer-satisfaction-2026.do"], entries.Keys);
        Golden.AssertMatches("stata/customer-satisfaction-2026.do", entries["customer-satisfaction-2026.do"]);
    }

    [Fact]
    public async Task Json_HasTheCodebookAndEveryResponse()
    {
        var bytes = await ExportAsync(new JsonExporter(), new ExportOptions(ExportLayout.Wide, ExportValues.Codes));

        Golden.AssertMatches("json/customer-satisfaction-2026.json", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Exporters_OnePerFormat_WithTheirFileTypes()
    {
        IExporter[] exporters = [new XlsxExporter(), new CsvExporter(), new SpssExporter(), new StataExporter(), new JsonExporter()];

        Assert.Equal(
            [(ExportFormat.Excel, ".xlsx"), (ExportFormat.Csv, ".csv"), (ExportFormat.Spss, ".zip"), (ExportFormat.Stata, ".zip"), (ExportFormat.Json, ".json")],
            exporters.Select(exporter => (exporter.Format, exporter.Extension)));
    }
}
