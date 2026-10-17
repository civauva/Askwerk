// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;
using Askwerk.Domain.Surveys;

namespace Askwerk.Domain.Messages;

/// <summary>Which message a text is for (AWK-17, W10 D).</summary>
public enum MessageKind
{
    /// <summary>The first email with the survey link.</summary>
    Invitation,

    /// <summary>A reminder before the deadline.</summary>
    Reminder,

    /// <summary>Thanks after answering.</summary>
    Thanks,
}

/// <summary>
/// An organisation's own text for one kind of message, per language, with placeholders such as {name} and {link}.
/// Without one, Askwerk's text in <see cref="DefaultMessages"/> is used.
/// </summary>
public sealed class MessageTemplate
{
    private MessageTemplate()
    {
        Subject = new TranslatedText(new Dictionary<string, string>());
        Body = new TranslatedText(new Dictionary<string, string>());
    }

    private MessageTemplate(Guid id, Guid tenantId, MessageKind kind, TranslatedText subject, TranslatedText body, bool answerInEmail, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Kind = kind;
        Subject = subject;
        Body = body;
        AnswerInEmail = answerInEmail;
        UpdatedAt = now;
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets which message it is.</summary>
    public MessageKind Kind { get; private set; }

    /// <summary>Gets the subject per language.</summary>
    public TranslatedText Subject { get; private set; }

    /// <summary>Gets the text per language; empty lines separate paragraphs, a line with only {link} becomes the button.</summary>
    public TranslatedText Body { get; private set; }

    /// <summary>Gets a value indicating whether the first question can be answered in the email itself.</summary>
    public bool AnswerInEmail { get; private set; }

    /// <summary>Gets when it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new text; the caller has checked the placeholders.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="kind">Which message.</param>
    /// <param name="subject">Subject per language.</param>
    /// <param name="body">Text per language.</param>
    /// <param name="answerInEmail">First question answerable in the email.</param>
    /// <param name="now">The time.</param>
    /// <returns>The template.</returns>
    public static MessageTemplate Create(Guid id, Guid tenantId, MessageKind kind, TranslatedText subject, TranslatedText body, bool answerInEmail, DateTimeOffset now) =>
        new(id, tenantId, kind, subject, body, answerInEmail, now);

    /// <summary>Replaces the texts.</summary>
    /// <param name="subject">Subject per language.</param>
    /// <param name="body">Text per language.</param>
    /// <param name="answerInEmail">First question answerable in the email.</param>
    /// <param name="now">The time.</param>
    public void Change(TranslatedText subject, TranslatedText body, bool answerInEmail, DateTimeOffset now)
    {
        Subject = subject;
        Body = body;
        AnswerInEmail = answerInEmail;
        UpdatedAt = now;
    }
}

/// <summary>The placeholders a message may use, and filling them in.</summary>
public static partial class Placeholders
{
    /// <summary>Every placeholder Askwerk fills in.</summary>
    public static readonly IReadOnlyList<string> Known = ["name", "link", "deadline", "duration", "organisation", "survey", "external_id"];

    /// <summary>Placeholders in a text that Askwerk does not know, e.g. "frist" for {frist}.</summary>
    /// <param name="text">A subject or body.</param>
    /// <param name="fields">The people's fields that are placeholders too (W35), e.g. department.</param>
    /// <returns>The unknown names.</returns>
    public static IEnumerable<string> Unknown(string text, IEnumerable<string>? fields = null)
    {
        var allowed = (fields ?? []).ToHashSet(StringComparer.Ordinal);

        return Placeholder().Matches(text ?? "").Select(match => match.Groups[1].Value).Where(name => !Known.Contains(name) && !allowed.Contains(name)).Distinct();
    }

    /// <summary>The text with every known placeholder replaced.</summary>
    /// <param name="text">A subject or body.</param>
    /// <param name="values">Values by placeholder name.</param>
    /// <returns>The filled-in text.</returns>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) => Fill(text, values, blankOthers: false);

    /// <summary>The text with every placeholder replaced; a field the person does not have becomes empty when asked (W35).</summary>
    /// <param name="text">A subject or body.</param>
    /// <param name="values">Values by placeholder name.</param>
    /// <param name="blankOthers">Replace placeholders without a value by nothing.</param>
    /// <returns>The filled-in text.</returns>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values, bool blankOthers) =>
        Placeholder().Replace(text ?? "", match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : blankOthers ? "" : match.Value);

    [GeneratedRegex(@"\{([a-zA-Z][a-zA-Z0-9_]*)\}")]
    private static partial Regex Placeholder();
}

/// <summary>Askwerk's own message texts in German and English (voice: short, factual, no exclamation marks in subjects).</summary>
public static class DefaultMessages
{
    /// <summary>Subject and body of a kind of message.</summary>
    /// <param name="kind">Which message.</param>
    /// <returns>Subject and body per language.</returns>
    public static (TranslatedText Subject, TranslatedText Body) For(MessageKind kind) => kind switch
    {
        MessageKind.Invitation => (
            Text("{organisation}: Ihre Meinung zählt", "{organisation}: your opinion counts"),
            Text(
                "Guten Tag {name},\n\nwir möchten wissen, wie zufrieden Sie mit uns sind. Die Umfrage „{survey}“ dauert etwa {duration}.\n\n{link}\n\nBitte antworten Sie bis {deadline}. Danke.\n{organisation}",
                "Hello {name},\n\nwe would like to know how satisfied you are with us. The survey “{survey}” takes about {duration}.\n\n{link}\n\nPlease answer by {deadline}. Thank you.\n{organisation}")),
        MessageKind.Reminder => (
            Text("Erinnerung: {survey}", "Reminder: {survey}"),
            Text(
                "Guten Tag {name},\n\nnoch bis {deadline} können Sie an „{survey}“ teilnehmen. Es dauert etwa {duration}.\n\n{link}\n\nDanke.\n{organisation}",
                "Hello {name},\n\nyou can still take part in “{survey}” until {deadline}. It takes about {duration}.\n\n{link}\n\nThank you.\n{organisation}")),
        _ => (
            Text("Danke für Ihre Antworten", "Thank you for your answers"),
            Text(
                "Guten Tag {name},\n\ndanke, dass Sie bei „{survey}“ mitgemacht haben. Ihre Antworten sind gespeichert.\n\n{organisation}",
                "Hello {name},\n\nthank you for taking part in “{survey}”. Your answers are saved.\n\n{organisation}")),
    };

    private static TranslatedText Text(string de, string en) => new(new Dictionary<string, string> { ["de"] = de, ["en"] = en });
}
