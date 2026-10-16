// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Askwerk.Domain.Surveys;

namespace Askwerk.Application.Exports;

/// <summary>The file formats results export to (agents/extensibility.md).</summary>
public enum ExportFormat
{
    /// <summary>An Excel workbook: the responses and the variables.</summary>
    Excel,

    /// <summary>Comma-separated values, UTF-8.</summary>
    Csv,

    /// <summary>CSV plus SPSS syntax (.sps) with variable labels, value labels and types, in a zip.</summary>
    Spss,

    /// <summary>CSV plus a Stata do-file with labels, in a zip.</summary>
    Stata,

    /// <summary>Askwerk JSON: the codebook and every response.</summary>
    Json,
}

/// <summary>Rows per response or per answer.</summary>
public enum ExportLayout
{
    /// <summary>One row per response, one column per variable.</summary>
    Wide,

    /// <summary>One row per answer: response, variable, value.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "Long is the statistics term for this layout, not a type.")]
    Long,
}

/// <summary>What a cell holds for variables with value labels.</summary>
public enum ExportValues
{
    /// <summary>The labels people read.</summary>
    Labels,

    /// <summary>The codes.</summary>
    Codes,

    /// <summary>The code, and the label in a column of its own.</summary>
    Both,
}

/// <summary>Choices of the export dialog's "More"; SPSS, Stata and JSON always write wide codes.</summary>
/// <param name="Layout">Wide or long.</param>
/// <param name="Values">Labels, codes or both.</param>
public sealed record ExportOptions(ExportLayout Layout, ExportValues Values);

/// <summary>One response to export.</summary>
/// <param name="Id">The response.</param>
/// <param name="Status">complete or partial.</param>
/// <param name="Version">The version answered.</param>
/// <param name="Language">The language answered in.</param>
/// <param name="StartedAt">Started (UTC).</param>
/// <param name="CompletedAt">Finished (UTC).</param>
/// <param name="Answers">Answers by question id.</param>
public sealed record ExportResponse(Guid Id, string Status, int Version, string Language, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, IReadOnlyDictionary<string, AnswerValue> Answers);

/// <summary>What an export writes: the codebook and the responses, read as they are written.</summary>
/// <param name="SurveyId">The survey.</param>
/// <param name="Title">Its name.</param>
/// <param name="FileName">The file's name without extension.</param>
/// <param name="Codebook">The codebook the export follows.</param>
/// <param name="ExportedAt">When (UTC).</param>
/// <param name="Responses">The responses, oldest first.</param>
public sealed record ExportData(Guid SurveyId, string Title, string FileName, Codebook Codebook, DateTimeOffset ExportedAt, IAsyncEnumerable<ExportResponse> Responses);

/// <summary>One file format of the results export (agents/extensibility.md); one class per format.</summary>
public interface IExporter
{
    /// <summary>The format.</summary>
    ExportFormat Format { get; }

    /// <summary>The file's media type.</summary>
    string ContentType { get; }

    /// <summary>The file's extension, with the dot.</summary>
    string Extension { get; }

    /// <summary>Writes the export.</summary>
    /// <param name="data">Codebook and responses.</param>
    /// <param name="options">Layout and values, where the format has a choice.</param>
    /// <param name="output">Where the file goes.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>When written.</returns>
    Task WriteAsync(ExportData data, ExportOptions options, Stream output, CancellationToken cancellationToken);
}

/// <summary>An export the format cannot hold, e.g. more rows than an Excel sheet has.</summary>
/// <param name="code">The problem code for the API.</param>
public sealed class ExportTooLargeException(string code) : Exception($"The export is too large: {code}.")
{
    /// <summary>The problem code.</summary>
    public string Code { get; } = code;
}

/// <summary>A column of a table export.</summary>
/// <param name="Name">The header.</param>
/// <param name="Label">What it means.</param>
/// <param name="Numeric">Whether every value is a number.</param>
/// <param name="Value">The cell of a response; null when empty.</param>
internal sealed record ExportColumn(string Name, string Label, bool Numeric, Func<ExportResponse, string?> Value);

/// <summary>The tables CSV, Excel, SPSS and Stata share: meta columns first, then the codebook's variables.</summary>
internal static class ExportTable
{
    /// <summary>The columns of a wide table.</summary>
    public static IReadOnlyList<ExportColumn> Wide(Codebook codebook, ExportValues values)
    {
        var columns = new List<ExportColumn>
        {
            new("response_id", "Response", false, response => response.Id.ToString()),
            new("status", "Status", false, response => response.Status),
            new("version", "Version", true, response => response.Version.ToString(CultureInfo.InvariantCulture)),
            new("language", "Language", false, response => response.Language),
            new("started_at", "Started (UTC)", false, response => Time(response.StartedAt)),
            new("completed_at", "Finished (UTC)", false, response => response.CompletedAt is { } completed ? Time(completed) : null),
            new("duration_seconds", "Time to finish (seconds)", true, response =>
                response.CompletedAt is { } completed ? ((long)Math.Round((completed - response.StartedAt).TotalSeconds)).ToString(CultureInfo.InvariantCulture) : null),
        };

        foreach (var variable in codebook.Variables)
        {
            string? Cell(ExportResponse response, bool labels) => codebook.Cell(variable, response.Answers.GetValueOrDefault(variable.QuestionKey), labels);

            switch (values)
            {
                case ExportValues.Codes:
                    columns.Add(new(variable.Name, variable.Label, variable.Numeric, response => Cell(response, false)));
                    break;
                case ExportValues.Both when variable.Values.Count > 0:
                    columns.Add(new(variable.Name, variable.Label, variable.Numeric, response => Cell(response, false)));
                    columns.Add(new($"{variable.Name}_label", variable.Label, false, response => Cell(response, true)));
                    break;
                default:
                    // With labels a numeric variable holds numbers and the labels of its codes (e.g. "Don't know").
                    columns.Add(new(variable.Name, variable.Label, variable.Numeric, response => Cell(response, values != ExportValues.Both)));
                    break;
            }
        }

        return columns;
    }

    /// <summary>The header of a long table.</summary>
    public static IReadOnlyList<string> LongHeader(ExportValues values) =>
        values == ExportValues.Both ? ["response_id", "variable", "value", "label"] : ["response_id", "variable", "value"];

    /// <summary>The rows of one response in a long table: one per answered variable.</summary>
    public static IEnumerable<string?[]> LongRows(Codebook codebook, ExportResponse response, ExportValues values)
    {
        foreach (var variable in codebook.Variables)
        {
            var answer = response.Answers.GetValueOrDefault(variable.QuestionKey);
            var code = codebook.Cell(variable, answer, labels: false);

            if (code is null)
            {
                continue;
            }

            var id = response.Id.ToString();
            var label = codebook.Cell(variable, answer, labels: true);

            if (values == ExportValues.Labels && label is null)
            {
                // An option not chosen has no label; with labels it is left out, as in the wide table.
                continue;
            }

            yield return values switch
            {
                ExportValues.Codes => [id, variable.Name, code],
                ExportValues.Both => [id, variable.Name, code, variable.Values.Count > 0 ? label : null],
                _ => [id, variable.Name, label],
            };
        }
    }

    /// <summary>A time as written in exports: ISO 8601, UTC, seconds.</summary>
    public static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
