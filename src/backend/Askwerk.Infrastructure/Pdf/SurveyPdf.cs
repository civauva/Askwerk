// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Askwerk.Infrastructure.Pdf;

/// <summary>What a survey PDF needs besides the definition and the theme.</summary>
/// <param name="Title">The survey's title in the language.</param>
/// <param name="Organisation">The organisation's name, for the footer and the document's author.</param>
/// <param name="Link">The survey's online address.</param>
/// <param name="Language">The language to print.</param>
/// <param name="Images">PNG or JPEG bytes by asset id, for the logo, header and question images.</param>
public sealed record SurveyPdfContent(string Title, string Organisation, string Link, string Language, IReadOnlyDictionary<string, byte[]> Images);

/// <summary>
/// A survey as a printable A4 form (AWK-17 step 7, W10 E), drawn with MigraDoc from the survey definition and its
/// theme: tick boxes, scales, lines to write on, tables for matrices, page numbers. Behind this class only, so our own
/// PDF writer can replace PDFsharp/MigraDoc in one place.
/// </summary>
public static class SurveyPdf
{
    /// <summary>The PDF bytes.</summary>
    /// <param name="definition">The survey.</param>
    /// <param name="theme">Its look.</param>
    /// <param name="content">Title, organisation, link, language and images.</param>
    /// <returns>A PDF file.</returns>
    public static byte[] Render(SurveyDefinition definition, ThemeTokens theme, SurveyPdfContent content)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);
        PdfFonts.Install();
        var german = content.Language == "de";
        string Text(TranslatedText? text) => text is null ? "" : text.TryGetValue(content.Language, out var value) && !string.IsNullOrWhiteSpace(value) ? value : text.TryGetValue(definition.DefaultLanguage, out var fallback) ? fallback : "";

        var document = new Document();
        document.Info.Title = content.Title;
        document.Info.Author = content.Organisation;
        var size = theme.Scale switch { ThemeScale.Xs => 9, ThemeScale.S => 9.5, ThemeScale.L => 12, ThemeScale.Xl => 13.5, _ => 10.5 };
        var accent = Colour(theme.Colours.Accent, Colors.Black);
        var ink = Colour(theme.Colours.Text, Colors.Black);
        var muted = Colour(theme.Colours.Muted, Colors.Gray);
        var line = Colour(theme.Colours.Line, Colors.LightGray);
        var body = PdfFonts.Usable(theme.Fonts.Body.Family);
        var heading = PdfFonts.Usable(theme.Fonts.Heading.Family);

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = body;
        normal.Font.Size = size;
        normal.Font.Color = ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(size * 0.6);

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        var width = Unit.FromCentimeter(17);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = muted;
        footer.Format.TabStops.AddTabStop(width, TabAlignment.Right);
        footer.AddText($"{content.Organisation} · {content.Title}\t{(german ? "Seite" : "Page")} ");
        footer.AddPageField();
        footer.AddText(german ? " von " : " of ");
        footer.AddNumPagesField();

        var header = definition.HeaderImage?.Asset ?? (theme.Layout is ThemeLayout.HeaderImage or ThemeLayout.SplitImage ? theme.HeaderImage?.Asset : null);

        // A drawn background (W28) prints as a band at the top of page 1; the pages stay white.
        if (theme.Pdf?.Header != PdfHeader.Plain && header is null && theme.Decoration?.Pattern == ThemePattern.Mountains)
        {
            var band = section.AddImage(Base64(PdfScenes.Mountains()));
            band.Width = width;
            band.LockAspectRatio = true;
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
        }

        // A plain header for printing (W26) leaves the header image out.
        if (theme.Pdf?.Header != PdfHeader.Plain && header is not null && content.Images.TryGetValue(header, out var headerBytes))
        {
            var image = section.AddImage(Base64(headerBytes));
            image.Width = width;
            image.LockAspectRatio = true;
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
        }

        if (theme.Logo?.Asset is { } logo && content.Images.TryGetValue(logo, out var logoBytes))
        {
            var image = section.AddImage(Base64(logoBytes));
            image.Height = Unit.FromCentimeter(1);
            image.LockAspectRatio = true;
        }

        var title = section.AddParagraph(content.Title);
        title.Format.Font.Name = heading;
        title.Format.Font.Size = size * 2;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromPoint(8);

        if (definition.Welcome?.Enabled != false && Text(definition.Welcome?.Text) is { Length: > 0 } welcome)
        {
            section.AddParagraph(welcome);
        }

        // The online line beside a QR code of the link, so a printed copy can still be answered on a phone.
        var onlineTable = section.AddTable();
        onlineTable.AddColumn(width - Unit.FromCentimeter(2.8));
        onlineTable.AddColumn(Unit.FromCentimeter(2.8));
        var onlineRow = onlineTable.AddRow();
        onlineRow.VerticalAlignment = VerticalAlignment.Center;
        var online = onlineRow.Cells[0].AddParagraph();
        online.Format.Font.Color = muted;
        online.AddText(german ? "Bitte ankreuzen. Online: " : "Please tick. Online: ");
        online.AddFormattedText(content.Link, new Font { Color = accent });
        var qr = onlineRow.Cells[1].AddImage(Base64(QrCode.Png(content.Link)));
        qr.Width = Unit.FromCentimeter(2.6);
        qr.LockAspectRatio = true;
        onlineRow.Cells[1].Format.Alignment = ParagraphAlignment.Right;
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(size * 0.8);

        var number = 0;

        // Logic in words (AWK-31, W19): a paper reader follows the rules the runner applies.
        void Rule(string sentence)
        {
            var rule = section.AddParagraph(sentence);
            rule.Format.Font.Color = muted;
            rule.Format.Font.Italic = true;
            rule.Format.KeepWithNext = true;
        }

        string Words(LogicGroup group) => LogicSentences.Of(group, definition, Text, german);

        foreach (var page in definition.Pages)
        {
            if (Text(page.Title) is { Length: > 0 } pageTitle)
            {
                var pageHeading = section.AddParagraph(pageTitle.ToUpper(CultureInfo.InvariantCulture));
                pageHeading.Format.Font.Size = size * 0.85;
                pageHeading.Format.Font.Bold = true;
                pageHeading.Format.Font.Color = muted;
                pageHeading.Format.SpaceBefore = Unit.FromPoint(size);
                pageHeading.Format.Borders.Bottom.Color = line;
                pageHeading.Format.Borders.Bottom.Width = 0.75;
            }

            if (page.ShowWhen is { Conditions.Count: > 0 } pageRule)
            {
                Rule(german ? $"Diese Seite nur, wenn {Words(pageRule)}." : $"This page only when {Words(pageRule)}.");
            }

            foreach (var question in page.Questions)
            {
                switch (question.Type)
                {
                    case QuestionType.TextBlock:
                        var textBlock = section.AddParagraph(Text(question.Text));

                        // Tinted text blocks (W28) print on a light tint with the accent edge, as on the web.
                        if (theme.Components?.TextBlock?.Style == TextBlockStyle.Tinted)
                        {
                            textBlock.Format.Shading.Color = Colour("#f7f2ec", Colors.WhiteSmoke);
                            textBlock.Format.Borders.Left.Color = accent;
                            textBlock.Format.Borders.Left.Width = Unit.FromPoint(2.5);
                            textBlock.Format.Borders.DistanceFromLeft = Unit.FromPoint(6);
                            textBlock.Format.LeftIndent = Unit.FromPoint(8);
                            textBlock.Format.SpaceBefore = Unit.FromPoint(size * 0.6);
                            textBlock.Format.SpaceAfter = Unit.FromPoint(size * 0.6);
                        }

                        continue;
                    case QuestionType.Image:
                        if (question.Image?.Asset is { } block && content.Images.TryGetValue(block, out var blockBytes))
                        {
                            var image = section.AddImage(Base64(blockBytes));
                            image.Width = question.Image.Size switch { ImageWidth.Small => width * 0.3, ImageWidth.Half => width * 0.5, ImageWidth.Wide => width * 0.8, _ => width };
                            image.LockAspectRatio = true;
                        }

                        if (Text(question.Text) is { Length: > 0 } caption)
                        {
                            var captionText = section.AddParagraph(caption);
                            captionText.Format.Font.Color = muted;
                        }

                        continue;
                }

                number++;
                var heading2 = section.AddParagraph();
                heading2.Format.KeepWithNext = true;
                heading2.Format.SpaceBefore = Unit.FromPoint(size * 0.8);
                if (theme.Pdf?.Numbered != false)
                {
                    heading2.AddFormattedText($"{number}  ", new Font { Bold = true, Color = accent });
                }

                heading2.AddFormattedText(Text(question.Text) + (question.Required ? " *" : ""), new Font { Bold = true, Name = heading });

                if (question.ShowWhen is { Conditions.Count: > 0 } questionRule)
                {
                    Rule(german ? $"Nur beantworten, wenn {Words(questionRule)}." : $"Answer only when {Words(questionRule)}.");
                }

                if (Text(question.Help) is { Length: > 0 } help)
                {
                    var helpText = section.AddParagraph(help);
                    helpText.Format.Font.Color = muted;
                    helpText.Format.KeepWithNext = true;
                }

                if (question.Image?.Asset is { } picture && content.Images.TryGetValue(picture, out var pictureBytes))
                {
                    var image = section.AddImage(Base64(pictureBytes));
                    image.Width = width * 0.5;
                    image.LockAspectRatio = true;
                }

                Answers(section, question, Text, german, width, line, size, content.Images);
            }

            if (page.After is { } after && (after.End == true || after.GoTo is not null))
            {
                var when = after.When is { Conditions.Count: > 0 } afterRule ? Words(afterRule) : null;
                var target = definition.Pages.ToList().FindIndex(each => each.Id == after.GoTo);
                var targetName = target < 0 ? "" : Text(definition.Pages[target].Title) is { Length: > 0 } named ? named : (german ? $"Seite {target + 1}" : $"page {target + 1}");

                Rule(after.End == true
                    ? (german ? (when is null ? "Die Umfrage endet hier." : $"Die Umfrage endet hier, wenn {when}.") : (when is null ? "The survey ends here." : $"The survey ends here when {when}."))
                    : (german ? (when is null ? $"Weiter mit „{targetName}“." : $"Weiter mit „{targetName}“, wenn {when}.") : (when is null ? $"Continue with “{targetName}”." : $"Continue with “{targetName}” when {when}.")));
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);

        return output.ToArray();
    }

    /// <summary>The place to answer, drawn for paper: boxes to tick, a scale, lines or a table.</summary>
    private static void Answers(Section section, Question question, Func<TranslatedText?, string> text, bool german, Unit width, Color line, double size, IReadOnlyDictionary<string, byte[]> images)
    {
        switch (question.Type)
        {
            case QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Dropdown:
                foreach (var option in question.Options ?? [])
                {
                    Tick(section, text(option.Label), line, size, option.Image?.Asset is { } asset && images.TryGetValue(asset, out var picture) ? picture : null);
                }

                if (question.Other is { } other)
                {
                    Tick(section, $"{text(other.Label)} ____________________", line, size);
                }

                break;
            case QuestionType.YesNo:
                Tick(section, text(question.Yes?.Label) is { Length: > 0 } yes ? yes : german ? "Ja" : "Yes", line, size);
                Tick(section, text(question.No?.Label) is { Length: > 0 } no ? no : german ? "Nein" : "No", line, size);
                break;
            case QuestionType.Rating or QuestionType.Nps or QuestionType.Slider:
                var (min, max) = question.Type == QuestionType.Nps ? (0, 10) : (question.Scale?.Min ?? 1, question.Scale?.Max ?? 5);
                var step = question.Type == QuestionType.Slider ? Math.Max(1, question.Scale?.Step ?? 1) : 1;
                var values = Enumerable.Range(0, ((max - min) / step) + 1).Select(index => min + (index * step)).ToList();

                if (values.Count > 11)
                {
                    WriteIn(section, line, 1, $"{min} – {max}");
                    break;
                }

                Scale(section, values, line, width, size);
                var ends = section.AddParagraph();
                ends.Format.Font.Size = size * 0.8;
                ends.Format.TabStops.AddTabStop(width, TabAlignment.Right);
                ends.AddText($"{text(question.Scale?.MinLabel)}\t{text(question.Scale?.MaxLabel)}");
                break;
            case QuestionType.Matrix:
                Matrix(section, question, text, line, width, size);
                break;
            case QuestionType.Ranking:
                foreach (var option in question.Options ?? [])
                {
                    Tick(section, text(option.Label), line, size);
                }

                var hint = section.AddParagraph(german ? "Bitte nummerieren: 1 = am wichtigsten." : "Please number them: 1 = most important.");
                hint.Format.Font.Size = size * 0.8;
                break;
            case QuestionType.LongText:
                WriteIn(section, line, 3, null);
                break;
            default:
                WriteIn(section, line, 1, null);
                break;
        }
    }

    private static void Tick(Section section, string label, Color line, double size, byte[]? picture = null)
    {
        // A square box to tick, then the label: a table keeps them on one line. A picture choice (W31) puts its
        // picture between them, with room around it.
        var box = Unit.FromPoint(size * 1.25);
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.AddColumn(Unit.FromPoint(box.Point + (size * 0.8)));

        if (picture is not null)
        {
            table.AddColumn(Unit.FromCentimeter(2.4));
        }

        table.AddColumn(Unit.FromCentimeter(picture is null ? 15 : 12.6));
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(picture is null ? size * 0.15 : 6);
        row.BottomPadding = Unit.FromPoint(picture is null ? 0 : 6);
        row.VerticalAlignment = VerticalAlignment.Center;
        var square = row.Cells[0].AddTextFrame();
        square.Width = box;
        square.Height = box;
        square.LineFormat.Color = line;
        square.LineFormat.Width = 0.75;

        if (picture is not null)
        {
            var image = row.Cells[1].AddImage(Base64(picture));
            image.Width = Unit.FromCentimeter(2);
            image.LockAspectRatio = true;
        }

        row.Cells[picture is null ? 1 : 2].AddParagraph(label);
    }

    private static void Scale(Section section, List<int> values, Color line, Unit width, double size)
    {
        var table = section.AddTable();
        table.Borders.Color = line;
        table.Borders.Width = 0.75;
        var cell = Unit.FromPoint(Math.Min(width.Point / values.Count, 48));

        foreach (var _ in values)
        {
            table.AddColumn(cell);
        }

        var row = table.AddRow();
        row.Height = Unit.FromPoint(size * 2.4);
        row.VerticalAlignment = VerticalAlignment.Center;

        for (var index = 0; index < values.Count; index++)
        {
            var paragraph = row.Cells[index].AddParagraph(values[index].ToString(CultureInfo.InvariantCulture));
            paragraph.Format.Alignment = ParagraphAlignment.Center;
        }
    }

    private static void Matrix(Section section, Question question, Func<TranslatedText?, string> text, Color line, Unit width, double size)
    {
        var options = question.Options ?? [];
        var table = section.AddTable();
        table.Borders.Color = line;
        table.Borders.Width = 0.5;
        var first = Unit.FromPoint(width.Point * 0.4);
        table.AddColumn(first);

        foreach (var _ in options)
        {
            table.AddColumn(Unit.FromPoint((width.Point - first.Point) / Math.Max(1, options.Count)));
        }

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Format.Font.Size = size * 0.85;
        head.Format.Font.Bold = true;

        for (var index = 0; index < options.Count; index++)
        {
            head.Cells[index + 1].AddParagraph(text(options[index].Label)).Format.Alignment = ParagraphAlignment.Center;
        }

        foreach (var matrixRow in question.Rows ?? [])
        {
            var row = table.AddRow();
            row.Height = Unit.FromPoint(size * 2);
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Cells[0].AddParagraph(text(matrixRow.Label));
        }
    }

    private static void WriteIn(Section section, Color line, int lines, string? hint)
    {
        for (var index = 0; index < lines; index++)
        {
            var paragraph = section.AddParagraph(index == 0 && hint is not null ? hint : " ");
            paragraph.Format.SpaceBefore = Unit.FromPoint(10);
            paragraph.Format.Borders.Bottom.Color = line;
            paragraph.Format.Borders.Bottom.Width = 0.75;
        }
    }

    private static Color Colour(string? hex, Color fallback) =>
        hex is { Length: 7 } && hex[0] == '#' && uint.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : fallback;

    private static string Base64(byte[] image) => "base64:" + Convert.ToBase64String(image);
}
