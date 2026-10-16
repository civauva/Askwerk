// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;

namespace Askwerk.Application.Exports;

/// <summary>
/// CSV (RFC 4180): UTF-8 with a byte order mark so spreadsheet programs read umlauts, commas, CRLF. Text that a
/// spreadsheet would run as a formula (=, +, -, @) gets a leading apostrophe.
/// </summary>
public sealed class CsvExporter : IExporter
{
    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Csv;

    /// <inheritdoc />
    public string ContentType => "text/csv";

    /// <inheritdoc />
    public string Extension => ".csv";

    /// <inheritdoc />
    public async Task WriteAsync(ExportData data, ExportOptions options, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await CsvTable.WriteAsync(writer, data, options, oneLine: false, cancellationToken);
    }
}

/// <summary>The CSV table CSV, SPSS and Stata write.</summary>
internal static class CsvTable
{
    /// <summary>Writes the table; returns the wide columns with the longest text per column (for SPSS widths).</summary>
    public static async Task<(IReadOnlyList<ExportColumn> Columns, int[] Widths, bool[] Decimals)> WriteAsync(
        TextWriter writer, ExportData data, ExportOptions options, bool oneLine, CancellationToken cancellationToken)
    {
        if (options.Layout == ExportLayout.Long)
        {
            await WriteRowAsync(writer, ExportTable.LongHeader(options.Values), oneLine, numeric: null);

            await foreach (var response in data.Responses.WithCancellation(cancellationToken))
            {
                foreach (var row in ExportTable.LongRows(data.Codebook, response, options.Values))
                {
                    await WriteRowAsync(writer, row, oneLine, numeric: null);
                }
            }

            return ([], [], []);
        }

        var columns = ExportTable.Wide(data.Codebook, options.Values);
        var widths = new int[columns.Count];
        var decimals = new bool[columns.Count];
        var numeric = columns.Select(column => column.Numeric).ToArray();
        await WriteRowAsync(writer, [.. columns.Select(column => column.Name)], oneLine, numeric: null);

        await foreach (var response in data.Responses.WithCancellation(cancellationToken))
        {
            var cells = new string?[columns.Count];

            for (var index = 0; index < columns.Count; index++)
            {
                var cell = columns[index].Value(response);
                cells[index] = cell;

                if (cell is not null)
                {
                    widths[index] = Math.Max(widths[index], Encoding.UTF8.GetByteCount(oneLine ? OneLine(cell) : cell));
                    decimals[index] |= cell.Contains('.', StringComparison.Ordinal);
                }
            }

            await WriteRowAsync(writer, cells, oneLine, numeric);
        }

        return (columns, widths, decimals);
    }

    private static async Task WriteRowAsync(TextWriter writer, IReadOnlyList<string?> cells, bool oneLine, bool[]? numeric)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
            {
                await writer.WriteAsync(',');
            }

            var cell = cells[index];

            if (cell is not null)
            {
                // Statistics programs read the text as it is; only spreadsheets need the formula guard.
                await writer.WriteAsync(oneLine ? Field(OneLine(cell), numeric: true) : Field(cell, numeric?[index] ?? false));
            }
        }

        await writer.WriteAsync("\r\n");
    }

    private static string Field(string cell, bool numeric)
    {
        // Text a spreadsheet would run as a formula is kept as text (CSV injection).
        var text = !numeric && cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !decimal.TryParse(cell, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _)
            ? "'" + cell
            : cell;

        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0 || text.StartsWith(' ') || text.EndsWith(' ')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    /// <summary>SPSS reads one case per line: line breaks in text become spaces.</summary>
    private static string OneLine(string cell) => cell.ReplaceLineEndings(" ");
}
