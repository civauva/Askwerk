// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Net;
using System.Text;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;

namespace Askwerk.Api.App.Messages;

/// <summary>What one email shows, already in the recipient's language.</summary>
/// <param name="Subject">Subject with placeholders filled in.</param>
/// <param name="Body">Text with placeholders filled in; empty lines separate paragraphs.</param>
/// <param name="Link">The survey link; a paragraph that is only this link becomes the button.</param>
/// <param name="StartLabel">The button's text.</param>
/// <param name="Organisation">The organisation's name, for the header and the footer.</param>
/// <param name="LogoUrl">Absolute address of the logo, if any.</param>
/// <param name="HeaderUrl">Absolute address of the header image, if any.</param>
/// <param name="HeaderAlt">The header image's alt text; empty for a theme's decorative image.</param>
/// <param name="FirstQuestion">The first question to answer in the email itself, if that is switched on and possible.</param>
/// <param name="UnsubscribeLabel">The unsubscribe link's text (AWK-7).</param>
/// <param name="UnsubscribeLink">The recipient's unsubscribe link; none in previews.</param>
public sealed record EmailContent(
    string Subject,
    string Body,
    string Link,
    string StartLabel,
    string Organisation,
    string? LogoUrl,
    string? HeaderUrl,
    string HeaderAlt,
    EmailQuestion? FirstQuestion,
    string? UnsubscribeLabel = null,
    string? UnsubscribeLink = null);

/// <summary>A question answered by clicking one of its answers, each a link into the survey.</summary>
/// <param name="Text">The question.</param>
/// <param name="Answers">Label and link per answer.</param>
public sealed record EmailQuestion(string Text, IReadOnlyList<(string Label, string Link)> Answers);

/// <summary>
/// Draws an email from the theme (AWK-17 step 6, W10 E): table layout and inline styles only, no scripts and no web
/// fonts, so mail clients show it as designed; a plain-text part says the same.
/// </summary>
public static class EmailRenderer
{
    /// <summary>The email's HTML and plain text.</summary>
    /// <param name="theme">The survey's look.</param>
    /// <param name="content">What it says.</param>
    /// <param name="language">Its language, for the lang attribute.</param>
    /// <returns>HTML and text.</returns>
    public static (string Html, string Text) Render(ThemeTokens theme, EmailContent content, string language)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);
        var c = theme.Colours;
        var radius = theme.Corners switch { ThemeCorners.Rounded => 8, ThemeCorners.Pill => 24, _ => 2 };
        var font = $"'{Attribute(theme.Fonts.Body.Family ?? "Arial")}', Arial, Helvetica, sans-serif";
        var heading = $"'{Attribute(theme.Fonts.Heading.Family ?? "Arial")}', Arial, Helvetica, sans-serif";
        var html = new StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<!doctype html><html lang=\"{Attribute(language)}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>{Encode(content.Subject)}</title></head>");
        html.Append(CultureInfo.InvariantCulture, $"<body style=\"margin:0;padding:0;background:{c.Background};\">");
        html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:{c.Background};\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background:{c.Surface};border:1px solid {c.Line};border-radius:{radius}px;\">");

        // The look's email header (W26): the logo as on the web, an accent band, or plain text only.
        var emailHeader = theme.Email?.Header ?? EmailHeader.Logo;

        if (emailHeader == EmailHeader.Band)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:18px 32px;background:{c.Accent};color:{c.OnAccent};font-family:{heading};font-size:18px;font-weight:bold;border-radius:{radius}px {radius}px 0 0;\">");
            html.Append(content.LogoUrl is { } bandLogo
                ? $"<img src=\"{Attribute(bandLogo)}\" height=\"32\" alt=\"{Attribute(content.Organisation)}\" style=\"display:block;height:32px;width:auto;border:0;\">"
                : Encode(content.Organisation));
            html.Append("</td></tr>");
        }

        if (emailHeader != EmailHeader.Plain && content.HeaderUrl is { } header)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td><img src=\"{Attribute(header)}\" width=\"600\" alt=\"{Attribute(content.HeaderAlt)}\" style=\"display:block;width:100%;height:auto;border:0;\"></td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:28px 32px;font-family:{font};font-size:16px;line-height:1.5;color:{c.Text};\">");

        if (emailHeader != EmailHeader.Band)
        {
            html.Append(emailHeader == EmailHeader.Logo && content.LogoUrl is { } logo
                ? $"<p style=\"margin:0 0 20px;\"><img src=\"{Attribute(logo)}\" height=\"36\" alt=\"{Attribute(content.Organisation)}\" style=\"display:block;height:36px;width:auto;border:0;\"></p>"
                : $"<p style=\"margin:0 0 20px;font-weight:bold;\">{Encode(content.Organisation)}</p>");
        }

        foreach (var paragraph in Paragraphs(content.Body))
        {
            if (paragraph.Trim() == content.Link)
            {
                if (content.FirstQuestion is { } question)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 12px;font-family:{heading};font-size:18px;font-weight:bold;\">{Encode(question.Text)}</p>");
                    html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 20px;\"><tr>");

                    foreach (var (label, link) in question.Answers)
                    {
                        html.Append(CultureInfo.InvariantCulture, $"<td style=\"padding:0 6px 6px 0;\"><a href=\"{Attribute(link)}\" style=\"display:inline-block;min-width:24px;padding:10px 12px;border:1px solid {c.Line};border-radius:{Math.Min(radius, 8)}px;background:{c.Surface};color:{c.Text};text-align:center;text-decoration:none;font-weight:bold;\">{Encode(label)}</a></td>");
                    }

                    html.Append("</tr></table>");
                }

                var filled = theme.Buttons == ThemeButtons.Outline ? $"background:{c.Surface};color:{c.Accent};" : $"background:{c.Accent};color:{c.OnAccent};";
                // Soft buttons (W26) stay filled in email: tints are unreliable in mail clients.
                html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 20px;\"><a href=\"{Attribute(content.Link)}\" style=\"display:inline-block;padding:12px 22px;border:2px solid {c.Accent};border-radius:{radius}px;{filled}text-decoration:none;font-weight:bold;\">{Encode(content.StartLabel)}</a></p>");
                continue;
            }

            html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 16px;\">{Linked(paragraph, content.Link, c.Accent)}</p>");
        }

        html.Append("</td></tr>");
        var unsubscribe = content.UnsubscribeLink is { } stop
            ? $" · <a href=\"{Attribute(stop)}\" style=\"color:{c.Muted};\">{Encode(content.UnsubscribeLabel ?? "Unsubscribe")}</a>"
            : "";
        html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:16px 32px;border-top:1px solid {c.Line};font-family:{font};font-size:12px;color:{c.Muted};\">{Encode(content.Organisation)}{unsubscribe}</td></tr>");
        html.Append("</table></td></tr></table></body></html>");

        var text = new StringBuilder();

        foreach (var paragraph in Paragraphs(content.Body))
        {
            if (paragraph.Trim() == content.Link && content.FirstQuestion is { } question)
            {
                text.Append(question.Text).Append('\n');

                foreach (var (label, link) in question.Answers)
                {
                    text.Append(CultureInfo.InvariantCulture, $"  {label}: {link}\n");
                }

                text.Append('\n');
            }

            text.Append(paragraph).Append("\n\n");
        }

        if (content.UnsubscribeLink is { } unsubscribeLink)
        {
            text.Append(CultureInfo.InvariantCulture, $"--\n{content.Organisation} · {content.UnsubscribeLabel ?? "Unsubscribe"}: {unsubscribeLink}\n");
        }

        return (html.ToString(), text.ToString().TrimEnd() + "\n");
    }

    private static string[] Paragraphs(string body) =>
        body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The paragraph as HTML: line breaks kept, the survey link clickable.</summary>
    private static string Linked(string paragraph, string link, string? accent) =>
        string.Join("<br>", paragraph.Split('\n').Select(line => Encode(line).Replace(Encode(link), $"<a href=\"{Attribute(link)}\" style=\"color:{accent};\">{Encode(link)}</a>", StringComparison.Ordinal)));

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string Attribute(string text) => WebUtility.HtmlEncode(text);
}
