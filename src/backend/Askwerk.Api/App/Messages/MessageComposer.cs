// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Askwerk.Api.App.Themes;
using Askwerk.Domain.Messages;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Messages;

/// <summary>One message to compose.</summary>
/// <param name="Kind">Invitation, reminder or thanks.</param>
/// <param name="Survey">The survey it is about.</param>
/// <param name="Language">The recipient's language; the survey's default when it does not offer it.</param>
/// <param name="Name">For {name}.</param>
/// <param name="Link">For {link} and the button: the survey link, personal for invitations.</param>
/// <param name="Deadline">For {deadline}.</param>
/// <param name="Unsubscribe">The recipient's unsubscribe link (AWK-7); null in previews and test sends.</param>
/// <param name="Subject">Texts not saved yet, for the preview.</param>
/// <param name="Body">Texts not saved yet, for the preview.</param>
/// <param name="AnswerInEmail">Not saved yet, for the preview.</param>
/// <param name="Fields">The person's further fields (W35), placeholders such as {department}.</param>
/// <param name="ExternalId">The person's id in another system (W35), for {external_id}.</param>
public sealed record MessageFor(
    MessageKind Kind,
    Survey Survey,
    string? Language,
    string Name,
    string Link,
    DateOnly Deadline,
    string? Unsubscribe,
    TranslatedText? Subject = null,
    TranslatedText? Body = null,
    bool? AnswerInEmail = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? ExternalId = null);

/// <summary>
/// Composes the organisation's messages for a survey (AWK-17 step 6, AWK-7): the stored or default texts with their
/// placeholders filled, drawn by <see cref="EmailRenderer"/> in the survey's theme. The preview, the test send and
/// the invitations all use it, so what the preview shows is what people get.
/// </summary>
public static class MessageComposer
{
    /// <summary>Subject, HTML and text of one message, in the recipient's language and the survey's theme.</summary>
    /// <param name="context">The database, in the survey's organisation.</param>
    /// <param name="baseAddress">The instance's address, for images.</param>
    /// <param name="message">What to compose.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The email.</returns>
    public static async Task<EmailPreviewResponse> ComposeAsync(AskwerkDbContext context, string baseAddress, MessageFor message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);
        var (kind, survey) = (message.Kind, message.Survey);
        var version = await context.SurveyVersions
            .Where(row => row.SurveyId == survey.Id && (row.Number == survey.PublishedVersion || row.State == SurveyVersionState.Draft))
            .OrderBy(row => row.State == SurveyVersionState.Draft)
            .FirstAsync(cancellationToken);
        var definition = version.ReadDefinition();
        var language = definition.Languages.Contains(message.Language ?? "") ? message.Language! : definition.DefaultLanguage;
        var german = language == "de";
        var stored = await context.MessageTemplates.FirstOrDefaultAsync(row => row.Kind == kind, cancellationToken);
        var (defaultSubject, defaultBody) = DefaultMessages.For(kind);
        var subject = In(message.Subject ?? stored?.Subject ?? defaultSubject, language, "");
        var body = In(message.Body ?? stored?.Body ?? defaultBody, language, "");
        var answerInEmail = message.AnswerInEmail ?? stored?.AnswerInEmail ?? kind == MessageKind.Invitation;
        var organisation = await context.Tenants.Where(tenant => tenant.Id == survey.TenantId).Select(tenant => tenant.Name).SingleAsync(cancellationToken);
        var link = message.Link;
        var answerable = definition.Pages.SelectMany(page => page.Questions).Where(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image)).ToList();
        var minutes = Math.Max(1, (int)Math.Round(answerable.Count * 0.3));
        // The person's fields first (W35), so the built-in placeholders win over a field of the same name.
        var values = new Dictionary<string, string>(message.Fields ?? new Dictionary<string, string>(), StringComparer.Ordinal);

        if (message.ExternalId is { } externalId)
        {
            values["external_id"] = externalId;
        }

        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["name"] = message.Name,
            ["link"] = link,
            ["deadline"] = LongDate(message.Deadline, german),
            ["duration"] = german ? (minutes == 1 ? "1 Minute" : $"{minutes} Minuten") : (minutes == 1 ? "1 minute" : $"{minutes} minutes"),
            ["organisation"] = organisation,
            ["survey"] = In(definition.Welcome?.Title, language, survey.Title),
        })
        {
            values[key] = value;
        }

        var theme = await SurveyThemes.EffectiveAsync(context, survey, cancellationToken);
        var asset = (string id, int width) => $"{baseAddress}/api/app/run/{survey.Id}/assets/{id}?w={width}";
        var header = definition.HeaderImage is { } own ? own.Asset : theme.Layout is ThemeLayout.HeaderImage or ThemeLayout.SplitImage ? theme.HeaderImage?.Asset : null;
        var content = new EmailContent(
            Placeholders.Fill(subject, values, blankOthers: true),
            Placeholders.Fill(body, values, blankOthers: true),
            link,
            german ? "Umfrage starten" : "Start the survey",
            organisation,
            theme.Logo?.Asset is { } logo ? asset(logo, 480) : null,
            header is null ? null : asset(header, 1600),
            definition.HeaderImage?.Alt is { } alt ? In(alt, language, "") : "",
            answerInEmail ? FirstQuestion(answerable.FirstOrDefault(), link, language, definition) : null,
            message.Unsubscribe is null ? null : german ? "Keine Einladungen mehr erhalten" : "Unsubscribe",
            message.Unsubscribe);
        var (html, text) = EmailRenderer.Render(theme, content, language);

        return new EmailPreviewResponse(content.Subject, html, text);
    }

    /// <summary>The first question as answer links, for rating, NPS, yes/no and single choice; null for other types.</summary>
    private static EmailQuestion? FirstQuestion(Question? question, string link, string language, SurveyDefinition definition)
    {
        if (question is null)
        {
            return null;
        }

        // A personal link already has ?t=; the answer goes after it.
        string Answer(string value) => $"{link}{(link.Contains('?', StringComparison.Ordinal) ? '&' : '?')}answer={question.Id}:{Uri.EscapeDataString(value)}";
        var german = language == "de";
        IReadOnlyList<(string Label, string Link)>? answers = question.Type switch
        {
            QuestionType.Rating when question.Scale is { } scale && scale.Max - scale.Min < 11 =>
                [.. Enumerable.Range(scale.Min, scale.Max - scale.Min + 1).Select(value => (value.ToString(CultureInfo.InvariantCulture), Answer(value.ToString(CultureInfo.InvariantCulture))))],
            QuestionType.Nps => [.. Enumerable.Range(0, 11).Select(value => (value.ToString(CultureInfo.InvariantCulture), Answer(value.ToString(CultureInfo.InvariantCulture))))],
            QuestionType.YesNo =>
            [
                (In(question.Yes?.Label, language, german ? "Ja" : "Yes"), Answer(question.Yes?.Code ?? "1")),
                (In(question.No?.Label, language, german ? "Nein" : "No"), Answer(question.No?.Code ?? "0")),
            ],
            QuestionType.SingleChoice when question.Options is { Count: <= 6 } options => [.. options.Select(option => (In(option.Label, language, option.Code), Answer(option.Code)))],
            _ => null,
        };

        return answers is null ? null : new EmailQuestion(In(question.Text, language, definition.DefaultLanguage), answers);
    }

    /// <summary>"28. Oktober 2026" or "28 October 2026", without culture data: the image runs with invariant globalisation.</summary>
    private static string LongDate(DateOnly date, bool german)
    {
        string[] months = german
            ? ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"]
            : ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

        return string.Create(CultureInfo.InvariantCulture, $"{date.Day}{(german ? "." : "")} {months[date.Month - 1]} {date.Year}");
    }

    private static string In(TranslatedText? text, string language, string fallback) =>
        text is not null && text.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value) ? value : text?.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? fallback;

}
