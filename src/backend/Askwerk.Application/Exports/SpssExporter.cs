// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Askwerk.Domain.Surveys;

namespace Askwerk.Application.Exports;

/// <summary>
/// SPSS: the codes as CSV plus a syntax file that reads it and applies types, variable labels, value labels and
/// "Don't know" as missing (agents/extensibility.md). Both in one zip; no native SPSS libraries.
/// </summary>
public sealed class SpssExporter : IExporter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Spss;

    /// <inheritdoc />
    public string ContentType => "application/zip";

    /// <inheritdoc />
    public string Extension => ".zip";

    /// <inheritdoc />
    public async Task WriteAsync(ExportData data, ExportOptions options, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var (columns, widths, decimals) = await SyntaxTable.WriteCsvAsync(zip, data, cancellationToken);
        await using var syntax = new StreamWriter(zip.CreateEntry($"{data.FileName}.sps", CompressionLevel.Optimal).Open(), Utf8);
        var variables = data.Codebook.Variables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);

        await syntax.WriteAsync($"""
            * Encoding: UTF-8.
            * Askwerk export of {Quote(data.Title)}, {ExportTable.Time(data.ExportedAt)}.
            * Put this file next to {data.FileName}.csv and run it.
            GET DATA
              /TYPE=TXT
              /FILE='{data.FileName}.csv'
              /ENCODING='UTF8'
              /ARRANGEMENT=DELIMITED
              /DELCASE=LINE
              /FIRSTCASE=2
              /DELIMITERS=','
              /QUALIFIER='"'
              /VARIABLES=

            """.ReplaceLineEndings("\n"));

        for (var index = 0; index < columns.Count; index++)
        {
            var type = columns[index].Numeric ? $"F{Math.Max(8, widths[index] + 1)}.{(decimals[index] ? 2 : 0)}" : $"A{Math.Max(1, widths[index])}";
            await syntax.WriteAsync($"    {columns[index].Name} {type}{(index == columns.Count - 1 ? "." : "")}\n");
        }

        await syntax.WriteAsync("\nVARIABLE LABELS\n");
        await syntax.WriteAsync(string.Join("\n  /", columns.Select(column => $"{column.Name} {Quote(Shorten(column.Label, 250))}")).Insert(0, "  ") + ".\n");

        var labelled = columns.Where(column => variables.GetValueOrDefault(column.Name) is { Values.Count: > 0 }).ToList();

        if (labelled.Count > 0)
        {
            await syntax.WriteAsync("\nVALUE LABELS\n  ");
            await syntax.WriteAsync(string.Join("\n  /", labelled.Select(column =>
            {
                var variable = variables[column.Name];
                return $"{column.Name} " + string.Join(" ", variable.Values.Select(value => $"{(column.Numeric ? value.Code : Quote(value.Code))} {Quote(Shorten(value.Label, 120))}"));
            })) + ".\n");
        }

        var missing = columns.Where(column => column.Numeric && variables.GetValueOrDefault(column.Name)?.Values.Any(value => value.Code == Codebook.DontKnowCode) == true).ToList();

        foreach (var column in missing)
        {
            await syntax.WriteAsync($"\nMISSING VALUES {column.Name} ({Codebook.DontKnowCode}).");
        }

        await syntax.WriteAsync($"{(missing.Count > 0 ? "\n" : "")}\nEXECUTE.\n");
    }

    /// <summary>A string in SPSS syntax: single quotes, doubled inside.</summary>
    private static string Quote(string text) => "'" + text.ReplaceLineEndings(" ").Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length];
}

/// <summary>The CSV part of the SPSS and Stata exports: wide, codes, one line per response.</summary>
internal static class SyntaxTable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<(IReadOnlyList<ExportColumn> Columns, int[] Widths, bool[] Decimals)> WriteCsvAsync(ZipArchive zip, ExportData data, CancellationToken cancellationToken)
    {
        await using var csv = new StreamWriter(zip.CreateEntry($"{data.FileName}.csv", CompressionLevel.Optimal).Open(), Utf8);

        return await CsvTable.WriteAsync(csv, data, new ExportOptions(ExportLayout.Wide, ExportValues.Codes), oneLine: true, cancellationToken);
    }

    /// <summary>Whether every value label of a variable has a whole-number code.</summary>
    public static bool WholeCodes(CodebookVariable variable) =>
        variable.Values.All(value => int.TryParse(value.Code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _));
}
