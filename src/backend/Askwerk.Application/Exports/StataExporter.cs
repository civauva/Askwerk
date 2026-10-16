// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Text;
using Askwerk.Domain.Surveys;

namespace Askwerk.Application.Exports;

/// <summary>
/// Stata: the codes as CSV plus a do-file that imports it and applies variable labels, value labels and
/// "Don't know" as the missing value .a (agents/extensibility.md). Both in one zip.
/// </summary>
public sealed class StataExporter : IExporter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Stata;

    /// <inheritdoc />
    public string ContentType => "application/zip";

    /// <inheritdoc />
    public string Extension => ".zip";

    /// <inheritdoc />
    public async Task WriteAsync(ExportData data, ExportOptions options, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var (columns, _, _) = await SyntaxTable.WriteCsvAsync(zip, data, cancellationToken);
        await using var script = new StreamWriter(zip.CreateEntry($"{data.FileName}.do", CompressionLevel.Optimal).Open(), Utf8);
        var variables = data.Codebook.Variables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);

        await script.WriteAsync($"""
            * Askwerk export of {data.Title.ReplaceLineEndings(" ")}, {ExportTable.Time(data.ExportedAt)}.
            * Put this file next to {data.FileName}.csv and run it.
            import delimited using "{data.FileName}.csv", varnames(1) encoding("utf-8") bindquote(strict) case(preserve) clear

            """.ReplaceLineEndings("\n"));

        foreach (var column in columns)
        {
            await script.WriteAsync($"label variable {column.Name} {Quote(Shorten(column.Label, 80))}\n");
        }

        foreach (var column in columns.Where(column => column.Numeric && variables.GetValueOrDefault(column.Name) is { Values.Count: > 0 } variable && SyntaxTable.WholeCodes(variable)))
        {
            var values = variables[column.Name].Values;

            if (values.Any(value => value.Code == Codebook.DontKnowCode))
            {
                await script.WriteAsync($"replace {column.Name} = .a if {column.Name} == {Codebook.DontKnowCode}\n");
            }

            var labels = values.Select(value => $"{(value.Code == Codebook.DontKnowCode ? ".a" : value.Code)} {Quote(Shorten(value.Label, 80))}");
            await script.WriteAsync($"label define {column.Name} {string.Join(" ", labels)}, replace\nlabel values {column.Name} {column.Name}\n");
        }
    }

    /// <summary>A string in Stata: compound quotes, so quotes inside need no escaping.</summary>
    private static string Quote(string text) => "`\"" + text.ReplaceLineEndings(" ") + "\"'";

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length];
}
