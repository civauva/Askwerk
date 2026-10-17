// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Askwerk.Application.Participants;

/// <summary>A person read from a list.</summary>
/// <param name="Email">Lower case.</param>
/// <param name="Name">May be empty.</param>
/// <param name="Language">May be empty.</param>
public sealed record ListedPerson(string Email, string? Name, string? Language);

/// <summary>A line that is not an address.</summary>
/// <param name="Line">1-based.</param>
/// <param name="Text">What it says.</param>
public sealed record ListProblem(int Line, string Text);

/// <summary>What a list holds.</summary>
/// <param name="People">Each address once, in the list's order.</param>
/// <param name="Duplicates">Addresses that came again.</param>
/// <param name="Invalid">Lines that are not addresses.</param>
public sealed record ParsedList(IReadOnlyList<ListedPerson> People, int Duplicates, IReadOnlyList<ListProblem> Invalid);

/// <summary>
/// Reads who to invite (AWK-7, W12 C step 1): pasted lines ("anna@acme.at" or "Anna Berger &lt;anna@acme.at&gt;"),
/// or a CSV or Excel file with a header row naming the email, name and language columns, in English or German.
/// </summary>
public static partial class ParticipantList
{
    private static readonly string[] EmailHeaders = ["email", "e-mail", "mail", "emailaddress", "e-mail-adresse", "adresse"];
    private static readonly string[] NameHeaders = ["name", "full name", "fullname", "vollständiger name"];
    private static readonly string[] LanguageHeaders = ["language", "lang", "sprache"];

    /// <summary>Pasted lines.</summary>
    /// <param name="text">One person per line.</param>
    /// <returns>The people, duplicates and bad lines.</returns>
    public static ParsedList FromText(string? text)
    {
        var rows = (text ?? "").ReplaceLineEndings("\n").Split('\n')
            .Select((line, index) => (Line: index + 1, Text: line.Trim()))
            .Where(row => row.Text.Length > 0)
            .Select(row =>
            {
                var named = NamedAddress().Match(row.Text);

                return named.Success
                    ? (row.Line, row.Text, Email: named.Groups["email"].Value, Name: named.Groups["name"].Value.Trim().Trim('"'), Language: (string?)null)
                    : (row.Line, row.Text, Email: row.Text, Name: (string?)null, Language: (string?)null);
            });

        return Collect(rows);
    }

    /// <summary>A CSV or Excel file with a header row.</summary>
    /// <param name="fileName">Its name; .xlsx is read as Excel, anything else as CSV.</param>
    /// <param name="content">Its bytes.</param>
    /// <returns>The people, duplicates and bad lines; null when the file has no email column.</returns>
    public static ParsedList? FromFile(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var rows = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadExcel(content) : ReadCsv(content);

        if (rows.Count == 0)
        {
            return null;
        }

        var header = rows[0].Select(cell => cell.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal)).ToList();
        int Column(string[] names) => header.FindIndex(cell => names.Contains(cell) || names.Contains(cell.Replace(" ", "", StringComparison.Ordinal)));
        var email = Column(EmailHeaders);

        if (email < 0)
        {
            return null;
        }

        var name = Column(NameHeaders);
        var language = Column(LanguageHeaders);
        string? Cell(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count && row[index].Trim().Length > 0 ? row[index].Trim() : null;

        return Collect(rows.Skip(1)
            .Select((row, index) => (Line: index + 2, Row: row))
            .Where(item => item.Row.Any(cell => cell.Trim().Length > 0))
            .Select(item => (item.Line, Text: string.Join(", ", item.Row), Email: Cell(item.Row, email) ?? "", Name: Cell(item.Row, name), Language: Cell(item.Row, language))));
    }

    /// <summary>A file as a table (W35): every row, the header first; .xlsx as Excel, anything else as CSV.</summary>
    /// <param name="fileName">Its name.</param>
    /// <param name="content">Its bytes.</param>
    /// <returns>The rows; empty when nothing can be read.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> ReadTable(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return Trimmed(fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadExcel(content) : ReadCsv(content));
    }

    /// <summary>A pasted table (W35): cells split by tabs, as Excel copies them, else read as CSV.</summary>
    /// <param name="text">The pasted text, the header first.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> ReadPasted(string? text)
    {
        var pasted = (text ?? "").ReplaceLineEndings("\n");

        return Trimmed(pasted.Contains('\t', StringComparison.Ordinal)
            ? [.. pasted.Split('\n').Select(line => line.Split('\t').ToList())]
            : ReadCsv(Encoding.UTF8.GetBytes(pasted)));
    }

    /// <summary>
    /// What a column most likely holds (W35), from its header in English or German: email, name, firstName,
    /// lastName, language, externalId, or field for anything else.
    /// </summary>
    /// <param name="header">The column's header.</param>
    /// <returns>The guess.</returns>
    public static string Guess(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var cell = header.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);
        var bare = cell.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        bool Is(params string[] names) => names.Contains(cell) || names.Contains(bare);

        return Is([.. EmailHeaders, "emailadresse"]) ? "email"
            : Is("first name", "firstname", "given name", "vorname") ? "firstName"
            : Is("last name", "lastname", "surname", "family name", "nachname", "familienname") ? "lastName"
            : Is(NameHeaders) ? "name"
            : Is(LanguageHeaders) ? "language"
            : Is("external id", "externalid", "external", "crm id", "crmid", "externe id", "externeid", "kundennummer", "customer number", "customerno", "customer no", "personalnummer", "employee id") ? "externalId"
            : "field";
    }

    /// <summary>Rows without the empty ones at the end.</summary>
    private static List<IReadOnlyList<string>> Trimmed(List<List<string>> rows) =>
        [.. rows.Select(row => (IReadOnlyList<string>)[.. row.Select(cell => cell.Trim())]).Where(row => row.Any(cell => cell.Length > 0))];

    /// <summary>Whether a text is an email address worth sending to.</summary>
    public static bool IsAddress(string email) => email.Length <= 254 && Address().IsMatch(email);

    private static ParsedList Collect(IEnumerable<(int Line, string Text, string Email, string? Name, string? Language)> rows)
    {
        var people = new List<ListedPerson>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var invalid = new List<ListProblem>();
        var duplicates = 0;

        foreach (var row in rows)
        {
            var email = row.Email.Trim().ToLowerInvariant();

            if (!IsAddress(email))
            {
                invalid.Add(new ListProblem(row.Line, row.Text));
            }
            else if (!seen.Add(email))
            {
                duplicates++;
            }
            else
            {
                people.Add(new ListedPerson(email, row.Name, row.Language is { Length: 2 } language ? language.ToLowerInvariant() : null));
            }
        }

        return new ParsedList(people, duplicates, invalid);
    }

    /// <summary>CSV with comma or semicolon (whichever the header uses), quotes as RFC 4180 writes them.</summary>
    private static List<List<string>> ReadCsv(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var firstLine = text.Split('\n')[0];
        var separator = firstLine.Count(character => character == ';') > firstLine.Count(character => character == ',') ? ';' : ',';
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (character == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                rows.Add(row);
                row = [];
                cell.Clear();
            }
            else
            {
                cell.Append(character);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().TrimEnd('\r'));
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The first sheet of a workbook: shared strings, inline strings and numbers, by column letter.</summary>
    private static List<List<string>> ReadExcel(byte[] content)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            XDocument Load(string name)
            {
                using var stream = (zip.GetEntry(name) ?? throw new InvalidDataException($"No {name}.")).Open();
                return XDocument.Load(stream);
            }

            var strings = zip.GetEntry("xl/sharedStrings.xml") is null
                ? []
                : Load("xl/sharedStrings.xml").Root!.Elements(main + "si").Select(item => string.Concat(item.Descendants(main + "t").Select(text => text.Value))).ToList();
            var firstSheet = Load("xl/workbook.xml").Root!.Element(main + "sheets")!.Elements(main + "sheet").First().Attribute(relationships + "id")!.Value;
            var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements().First(relationship => relationship.Attribute("Id")?.Value == firstSheet).Attribute("Target")!.Value;
            var sheet = Load(target.StartsWith('/') ? target.TrimStart('/') : $"xl/{target}");

            return [.. sheet.Descendants(main + "row").Select(row =>
            {
                var cells = new List<string>();

                foreach (var cell in row.Elements(main + "c"))
                {
                    var reference = cell.Attribute("r")?.Value ?? "";
                    var column = reference.TakeWhile(char.IsAsciiLetter).Aggregate(0, (value, letter) => (value * 26) + (char.ToUpperInvariant(letter) - 'A' + 1)) - 1;
                    var value = cell.Attribute("t")?.Value switch
                    {
                        "s" => strings[int.Parse(cell.Element(main + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture)],
                        "inlineStr" => string.Concat(cell.Descendants(main + "t").Select(text => text.Value)),
                        _ => cell.Element(main + "v")?.Value ?? "",
                    };

                    while (cells.Count < Math.Max(column, cells.Count))
                    {
                        cells.Add("");
                    }

                    cells.Add(value);
                }

                return cells;
            })];
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or NullReferenceException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            return [];
        }
    }

    [GeneratedRegex("^(?<name>[^<>]*)<(?<email>[^<>]+)>$")]
    private static partial Regex NamedAddress();

    [GeneratedRegex("^[^@\\s<>\",;]+@[^@\\s<>\",;]+\\.[a-z]{2,}$")]
    private static partial Regex Address();
}
