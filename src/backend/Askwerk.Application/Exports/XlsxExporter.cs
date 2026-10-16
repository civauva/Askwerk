// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Askwerk.Application.Exports;

/// <summary>
/// Excel (.xlsx) by our own small writer (decisions.md, AWK-24): Office Open XML is a zip of a few XML files, so no
/// library is needed. Sheet "Responses" holds the table with a frozen header; sheet "Variables" the codebook.
/// Text is written inline, never as a formula.
/// </summary>
public sealed class XlsxExporter : IExporter
{
    /// <summary>Rows an Excel sheet holds, the header included.</summary>
    public const int MaxRows = 1_048_576;

    private const int MaxCellText = 32_767;
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Excel;

    /// <inheritdoc />
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <inheritdoc />
    public string Extension => ".xlsx";

    /// <inheritdoc />
    public async Task WriteAsync(ExportData data, ExportOptions options, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await PartAsync(zip, "[Content_Types].xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
            """);
        await PartAsync(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
            """);
        await PartAsync(zip, "xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="{Main}" xmlns:r="{Relationships}"><sheets><sheet name="Responses" sheetId="1" r:id="rId1"/><sheet name="Variables" sheetId="2" r:id="rId2"/></sheets></workbook>
            """);
        await PartAsync(zip, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
            """);
        await PartAsync(zip, "xl/styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="{Main}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
            """);

        await using (var sheet = Sheet(zip, "xl/worksheets/sheet1.xml"))
        {
            var rows = 1;

            if (options.Layout == ExportLayout.Long)
            {
                var header = ExportTable.LongHeader(options.Values);
                await RowAsync(sheet, rows, [.. header.Select(name => (name, false))], bold: true);

                await foreach (var response in data.Responses.WithCancellation(cancellationToken))
                {
                    foreach (var row in ExportTable.LongRows(data.Codebook, response, options.Values))
                    {
                        Count(++rows);
                        await RowAsync(sheet, rows, [.. row.Select(cell => (cell, false))], bold: false);
                    }
                }
            }
            else
            {
                var columns = ExportTable.Wide(data.Codebook, options.Values);
                await RowAsync(sheet, rows, [.. columns.Select(column => ((string?)column.Name, false))], bold: true);

                await foreach (var response in data.Responses.WithCancellation(cancellationToken))
                {
                    Count(++rows);
                    await RowAsync(sheet, rows, [.. columns.Select(column => (column.Value(response), column.Numeric))], bold: false);
                }
            }

            await sheet.WriteAsync("</sheetData></worksheet>");
        }

        await using (var sheet = Sheet(zip, "xl/worksheets/sheet2.xml"))
        {
            await RowAsync(sheet, 1, [("variable", false), ("label", false), ("type", false), ("values", false)], bold: true);
            var row = 1;

            foreach (var variable in data.Codebook.Variables)
            {
                await RowAsync(
                    sheet,
                    ++row,
                    [(variable.Name, false), (variable.Label, false), (variable.Numeric ? "number" : "text", false), (string.Join("; ", variable.Values.Select(value => $"{value.Code} = {value.Label}")), false)],
                    bold: false);
            }

            await sheet.WriteAsync("</sheetData></worksheet>");
        }
    }

    private static void Count(int rows)
    {
        if (rows > MaxRows)
        {
            throw new ExportTooLargeException("too-many-rows-for-excel");
        }
    }

    private static async Task PartAsync(ZipArchive zip, string name, string xml)
    {
        await using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Utf8);
        await writer.WriteAsync(xml.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    private static StreamWriter Sheet(ZipArchive zip, string name)
    {
        var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Utf8);
        writer.Write($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\n"}<worksheet xmlns="{Main}"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetData>""");

        return writer;
    }

    private static async Task RowAsync(TextWriter sheet, int row, (string? Text, bool Numeric)[] cells, bool bold)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"<row r=\"{row}\">");

        for (var index = 0; index < cells.Length; index++)
        {
            var (text, numeric) = cells[index];

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var reference = Column(index) + row.ToString(CultureInfo.InvariantCulture);

            if (numeric && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                line.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{text}</v></c>");
                continue;
            }

            var clean = Clean(text);
            var space = clean.Length > 0 && (char.IsWhiteSpace(clean[0]) || char.IsWhiteSpace(clean[^1]) || clean.Contains('\n', StringComparison.Ordinal)) ? " xml:space=\"preserve\"" : "";
            line.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"{(bold ? " s=\"1\"" : "")}><is><t{space}>{SecurityElement.Escape(clean)}</t></is></c>");
        }

        line.Append("</row>");
        await sheet.WriteAsync(line.ToString());
    }

    /// <summary>Excel's column letters: A … Z, AA … .</summary>
    private static string Column(int index)
    {
        var name = "";

        for (var number = index + 1; number > 0; number = (number - 1) / 26)
        {
            name = (char)('A' + ((number - 1) % 26)) + name;
        }

        return name;
    }

    /// <summary>Text as XML allows it and Excel keeps it: no control characters, at most 32,767 characters.</summary>
    private static string Clean(string text)
    {
        var clean = new StringBuilder(Math.Min(text.Length, MaxCellText));

        foreach (var character in text)
        {
            if (clean.Length == MaxCellText)
            {
                break;
            }

            if (character is '\t' or '\n' || character >= ' ' && character is not ('￾' or '￿'))
            {
                clean.Append(character);
            }
        }

        return clean.ToString();
    }
}
