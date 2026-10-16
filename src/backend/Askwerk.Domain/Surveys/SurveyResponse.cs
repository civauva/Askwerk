// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Surveys;

/// <summary>Where a response stands.</summary>
public enum ResponseStatus
{
    /// <summary>Started, not finished; answers are kept page by page.</summary>
    Partial,

    /// <summary>Finished.</summary>
    Complete,
}

/// <summary>
/// One respondent's answers to one published version (agents/data-model.md, Collection). The runner finds it again
/// through a resume token in the respondent's browser; only its hash is stored. Collector and participant follow
/// with distribution.
/// </summary>
public sealed class SurveyResponse
{
    // For EF Core.
    private SurveyResponse()
    {
        Language = "";
        PageKey = "";
        ResumeTokenHash = "";
    }

    private SurveyResponse(Guid id, Guid tenantId, Guid surveyId, Guid surveyVersionId, string language, string pageKey, string resumeTokenHash, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        SurveyId = surveyId;
        SurveyVersionId = surveyVersionId;
        Language = language;
        PageKey = pageKey;
        ResumeTokenHash = resumeTokenHash;
        StartedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The survey.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>The published version answered (data-model.md, rule 4: never a draft).</summary>
    public Guid SurveyVersionId { get; private set; }

    /// <summary>The invited person whose personal link started it (AWK-7); null for the open link.</summary>
    public Guid? ParticipantId { get; private set; }

    /// <summary>Partial or complete.</summary>
    public ResponseStatus Status { get; private set; }

    /// <summary>The language the respondent answers in.</summary>
    public string Language { get; private set; }

    /// <summary>The page to show when the respondent comes back.</summary>
    public string PageKey { get; private set; }

    /// <summary>SHA-256 of the resume token in the respondent's browser.</summary>
    public string ResumeTokenHash { get; private set; }

    /// <summary>When it was started (UTC).</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When it was finished (UTC).</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Monotonic change number for exact "changed since" pulls; the database sets it.</summary>
    public long ChangeSeq { get; private set; }

    /// <summary>Starts a response on the first page.</summary>
    public static SurveyResponse Start(Guid id, Guid tenantId, Guid surveyId, Guid surveyVersionId, string language, string firstPage, string resumeTokenHash, DateTimeOffset now) =>
        new(id, tenantId, surveyId, surveyVersionId, language, firstPage, resumeTokenHash, now);

    /// <summary>Keeps the response with the invited person who started it.</summary>
    /// <param name="participantId">The participant.</param>
    public void StartedBy(Guid participantId) => ParticipantId = participantId;

    /// <summary>Gets the host system's id of the person, when a host product named them (AWK-13, W21).</summary>
    public string? RespondentExternalId { get; private set; }

    /// <summary>Gets the person's email address as the host product named it.</summary>
    public string? RespondentEmail { get; private set; }

    /// <summary>Keeps the response with the person a host product named in its token.</summary>
    /// <param name="externalId">The host's user id.</param>
    /// <param name="email">Their email address.</param>
    public void IdentifiedBy(string externalId, string email)
    {
        RespondentExternalId = externalId;
        RespondentEmail = email;
    }

    /// <summary>A page was saved; the respondent continues on the given page.</summary>
    public void PageSaved(string nextPage, DateTimeOffset now)
    {
        EnsurePartial();
        PageKey = nextPage;
        UpdatedAt = now;
    }

    /// <summary>Finishes the response.</summary>
    public void Complete(DateTimeOffset now)
    {
        EnsurePartial();
        Status = ResponseStatus.Complete;
        CompletedAt = now;
        UpdatedAt = now;
    }

    private void EnsurePartial()
    {
        if (Status != ResponseStatus.Partial)
        {
            throw new InvalidOperationException("A complete response does not change.");
        }
    }
}

/// <summary>
/// One answer: the source of truth for answers, one row per response and question with typed columns
/// (data-model.md). Structured parts (the "Other" text, matrix rows, "Don't know") go into detail.
/// </summary>
public sealed class Answer
{
    // For EF Core.
    private Answer()
    {
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The response.</summary>
    public Guid ResponseId { get; private set; }

    /// <summary>The question row of the published version's index.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>Rating, NPS, slider and number answers.</summary>
    public decimal? NumberValue { get; private set; }

    /// <summary>Text answers.</summary>
    public string? TextValue { get; private set; }

    /// <summary>Date, date-time or time as written for the mode.</summary>
    public string? DateValue { get; private set; }

    /// <summary>Chosen option codes; for ranking in ranked order.</summary>
    public string[]? OptionCodes { get; private set; }

    /// <summary>JSON: {"other": …}, {"rows": {…}}, {"dontKnow": true}.</summary>
    public string? Detail { get; private set; }

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new answer row.</summary>
    public static Answer Create(Guid id, Guid tenantId, Guid responseId, Guid questionId) =>
        new() { Id = id, TenantId = tenantId, ResponseId = responseId, QuestionId = questionId };

    /// <summary>Stores the value in the typed columns for the question's type.</summary>
    public void Set(QuestionType type, AnswerValue value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(value);
        NumberValue = value.Number;
        TextValue = string.IsNullOrWhiteSpace(value.Text) ? null : value.Text.Trim();
        DateValue = string.IsNullOrWhiteSpace(value.Date) ? null : value.Date.Trim();
        OptionCodes = type switch
        {
            QuestionType.Ranking => value.Order?.ToArray(),
            QuestionType.MultipleChoice => value.Options?.ToArray(),
            _ when value.Option is not null => [value.Option],
            _ => null,
        };
        var detail = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(value.Other))
        {
            detail["other"] = value.Other.Trim();
        }

        if (value.Rows is { Count: > 0 })
        {
            detail["rows"] = value.Rows;
        }

        if (value.DontKnow == true)
        {
            detail["dontKnow"] = true;
        }

        Detail = detail.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(detail);
        UpdatedAt = now;
    }

    /// <summary>The value as the runner sends it, for resuming.</summary>
    public AnswerValue ToValue(QuestionType type)
    {
        using var detail = System.Text.Json.JsonDocument.Parse(Detail ?? "{}");
        var root = detail.RootElement;
        var other = root.TryGetProperty("other", out var otherText) ? otherText.GetString() : null;
        var rows = root.TryGetProperty("rows", out var rowsElement)
            ? rowsElement.EnumerateObject().ToDictionary(row => row.Name, row => row.Value.GetString() ?? "", StringComparer.Ordinal)
            : null;
        var dontKnow = root.TryGetProperty("dontKnow", out _) ? true : (bool?)null;

        return type switch
        {
            QuestionType.Ranking => new AnswerValue(Order: OptionCodes, DontKnow: dontKnow),
            QuestionType.MultipleChoice => new AnswerValue(Options: OptionCodes, Other: other, DontKnow: dontKnow),
            QuestionType.Matrix => new AnswerValue(Rows: rows, DontKnow: dontKnow),
            _ => new AnswerValue(Option: OptionCodes?.FirstOrDefault(), Other: other, Number: NumberValue, Text: TextValue, Date: DateValue, DontKnow: dontKnow),
        };
    }
}
