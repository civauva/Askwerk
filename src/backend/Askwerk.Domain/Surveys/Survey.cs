// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Identity;

namespace Askwerk.Domain.Surveys;

/// <summary>Where a survey stands as a whole (agents/data-model.md, Survey definition).</summary>
public enum SurveyStatus
{
    /// <summary>Never published.</summary>
    Draft,

    /// <summary>At least one version is published.</summary>
    Published,

    /// <summary>No longer collecting.</summary>
    Closed,

    /// <summary>Put away.</summary>
    Archived,
}

/// <summary>
/// A survey of one organisation (AWK-15 step 3, W6). Its content lives in versions; exactly one draft exists at a
/// time, and published versions never change.
/// </summary>
public sealed class Survey
{
    // For EF Core.
    private Survey()
    {
        Title = "";
        Code = "";
        Languages = [];
        DefaultLanguage = "";
    }

    private Survey(Guid id, Guid tenantId, string title, string[] languages, string defaultLanguage, Guid createdBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Code = ShortCode.New();
        Title = title;
        Languages = languages;
        DefaultLanguage = defaultLanguage;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The short code in its links (AWK-7): /s/{code}; unique across the instance.</summary>
    public string Code { get; private set; }

    /// <summary>Internal name shown in the list and the designer, not to respondents.</summary>
    public string Title { get; private set; }

    /// <summary>Overall status.</summary>
    public SurveyStatus Status { get; private set; }

    /// <summary>Number of the latest published version, if any.</summary>
    public int? PublishedVersion { get; private set; }

    /// <summary>Languages of the current draft.</summary>
    public string[] Languages { get; private set; }

    /// <summary>Default language of the current draft.</summary>
    public string DefaultLanguage { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>When it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last change of the survey or its draft (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Soft delete (UTC).</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Gets the theme the survey uses; null for the organisation's default (AWK-17).</summary>
    public Guid? ThemeId { get; private set; }

    /// <summary>Gets the survey's own look as theme tokens JSON (W26); wins over <see cref="ThemeId"/> when set.</summary>
    public string? Look { get; private set; }

    /// <summary>Gets the look the own look started from (W26): "preset:konfetti" or "theme:{id}", for the gallery and its reset.</summary>
    public string? LookBase { get; private set; }

    /// <summary>Creates a survey and its first draft: one empty page in the given languages.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="title">Internal name.</param>
    /// <param name="languages">Languages offered.</param>
    /// <param name="defaultLanguage">One of the languages.</param>
    /// <param name="createdBy">The user.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The survey and its draft version.</returns>
    public static (Survey Survey, SurveyVersion Draft) Create(Guid id, Guid tenantId, string title, IReadOnlyList<string> languages, string defaultLanguage, Guid createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(languages);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainValidationException("title", "title-required");
        }

        if (title.Trim().Length > 200)
        {
            throw new DomainValidationException("title", "title-too-long");
        }

        if (languages.Count == 0)
        {
            throw new DomainValidationException("languages", "languages-required");
        }

        if (!languages.Contains(defaultLanguage))
        {
            throw new DomainValidationException("defaultLanguage", "default-language-not-offered");
        }

        var survey = new Survey(id, tenantId, title.Trim(), [.. languages.Distinct()], defaultLanguage, createdBy, now);
        var definition = new SurveyDefinition(1, survey.Languages, defaultLanguage, [new Page("p1", [])]);

        return (survey, SurveyVersion.NewDraft(Guid.CreateVersion7(now), survey, 1, definition, createdBy, now));
    }

    /// <summary>Takes over the draft's languages after a save.</summary>
    /// <param name="definition">The saved definition.</param>
    /// <param name="now">Time of the save.</param>
    public void DraftSaved(SurveyDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Languages = [.. definition.Languages];
        DefaultLanguage = definition.DefaultLanguage;
        UpdatedAt = now;
    }

    /// <summary>Gives the survey a new internal title (AWK-29); respondents never see it.</summary>
    /// <param name="title">The new title.</param>
    /// <param name="now">The time.</param>
    public void Rename(string title, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainValidationException("title", "title-required");
        }

        if (title.Trim().Length > 200)
        {
            throw new DomainValidationException("title", "title-too-long");
        }

        Title = title.Trim();
        UpdatedAt = now;
    }

    /// <summary>Stops taking responses; only a published survey can close.</summary>
    /// <param name="now">The time.</param>
    public void Close(DateTimeOffset now)
    {
        if (Status != SurveyStatus.Published)
        {
            throw new InvalidOperationException("Only a published survey can close.");
        }

        Status = SurveyStatus.Closed;
        UpdatedAt = now;
    }

    /// <summary>Takes responses again after closing, by hand or by the closing date; a passed closing date goes.</summary>
    /// <param name="now">The time.</param>
    public void Reopen(DateTimeOffset now)
    {
        if (StatusAt(now) != SurveyStatus.Closed)
        {
            throw new InvalidOperationException("Only a closed survey can reopen.");
        }

        // A closing date that has passed would close it again at once (W24).
        if (ClosesAt <= now)
        {
            ClosesAt = null;
        }

        Status = SurveyStatus.Published;
        UpdatedAt = now;
    }

    /// <summary>Gets the moment the survey stops taking responses by itself (UTC); null for none (AWK-29, W24).</summary>
    public DateTimeOffset? ClosesAt { get; private set; }

    /// <summary>Gets a value indicating whether a browser that finished may not answer again (AWK-29, W24); on by default.</summary>
    public bool OneResponsePerPerson { get; private set; } = true;

    /// <summary>Sets or removes the closing date.</summary>
    /// <param name="closesAt">A moment in the future, or null for none.</param>
    /// <param name="now">The time.</param>
    public void SetClosingDate(DateTimeOffset? closesAt, DateTimeOffset now)
    {
        if (closesAt <= now)
        {
            throw new DomainValidationException("closesAt", "closing-date-past");
        }

        ClosesAt = closesAt;
        UpdatedAt = now;
    }

    /// <summary>Whether responses are taken at this moment: published and the closing date not reached.</summary>
    /// <param name="now">The moment.</param>
    /// <returns>True while collecting.</returns>
    public bool IsCollecting(DateTimeOffset now) => StatusAt(now) == SurveyStatus.Published;

    /// <summary>The status at this moment: closed once the closing date has passed, even before the job closes it.</summary>
    /// <param name="now">The moment.</param>
    /// <returns>The status respondents and the designer see.</returns>
    public SurveyStatus StatusAt(DateTimeOffset now) => Status == SurveyStatus.Published && ClosesAt <= now ? SurveyStatus.Closed : Status;

    /// <summary>Closes a published survey whose closing date has passed (the background job).</summary>
    /// <param name="now">The time.</param>
    /// <returns>True when it closed now.</returns>
    public bool CloseAtClosingDate(DateTimeOffset now)
    {
        if (Status != SurveyStatus.Published || !(ClosesAt <= now))
        {
            return false;
        }

        Status = SurveyStatus.Closed;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Turns one response per person on or off.</summary>
    /// <param name="on">On or off.</param>
    /// <param name="now">The time.</param>
    public void AllowOneResponsePerPerson(bool on, DateTimeOffset now)
    {
        OneResponsePerPerson = on;
        UpdatedAt = now;
    }

    /// <summary>Puts the survey away: out of the list, no responses. Its results and versions stay.</summary>
    /// <param name="now">The time.</param>
    public void Archive(DateTimeOffset now)
    {
        Status = SurveyStatus.Archived;
        UpdatedAt = now;
    }

    /// <summary>Gets the websites that may show this survey (AWK-13); empty uses the organisation's list.</summary>
    public string[] EmbedOrigins { get; private set; } = [];

    /// <summary>Narrows the websites that may show this survey; none uses the organisation's list.</summary>
    /// <param name="origins">Origins, each one of the organisation's.</param>
    /// <param name="now">When.</param>
    public void NarrowEmbedOrigins(IEnumerable<string> origins, DateTimeOffset now)
    {
        EmbedOrigins = [.. origins];
        UpdatedAt = now;
    }

    /// <summary>Uses a theme, or the organisation's default when null. Not part of a version: published surveys follow at once.</summary>
    /// <param name="themeId">The theme, or null.</param>
    /// <param name="now">The time.</param>
    public void UseTheme(Guid? themeId, DateTimeOffset now)
    {
        ThemeId = themeId;
        Look = null;
        LookBase = null;
        UpdatedAt = now;
    }

    /// <summary>Uses its own look (W26), checked by the caller; not part of a version, so published surveys follow at once.</summary>
    /// <param name="tokens">Theme tokens as JSON.</param>
    /// <param name="lookBase">The look it started from, or null.</param>
    /// <param name="now">The time.</param>
    public void UseLook(string tokens, string? lookBase, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokens);
        Look = tokens;
        LookBase = string.IsNullOrWhiteSpace(lookBase) ? null : lookBase.Trim();
        UpdatedAt = now;
    }

    /// <summary>Publishes the draft and starts the next one with the same content. The publish checks run before this.</summary>
    /// <param name="draft">This survey's draft.</param>
    /// <param name="nextId">Id of the next draft.</param>
    /// <param name="userId">Who publishes.</param>
    /// <param name="now">Time of publishing.</param>
    /// <returns>The next draft.</returns>
    public SurveyVersion Publish(SurveyVersion draft, Guid nextId, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.SurveyId != Id || draft.State != SurveyVersionState.Draft)
        {
            throw new InvalidOperationException("Only this survey's draft can be published.");
        }

        draft.MarkPublished(now);
        Status = SurveyStatus.Published;
        PublishedVersion = draft.Number;
        UpdatedAt = now;

        return SurveyVersion.NewDraft(nextId, this, draft.Number + 1, draft.ReadDefinition(), userId, now);
    }
}

/// <summary>Whether a version can still change.</summary>
public enum SurveyVersionState
{
    /// <summary>The one editable version.</summary>
    Draft,

    /// <summary>Frozen; responses refer to it.</summary>
    Published,
}

/// <summary>One version of a survey's definition; the JSON is the source of truth (data-model.md).</summary>
public sealed class SurveyVersion
{
    // For EF Core.
    private SurveyVersion()
    {
        Definition = "";
    }

    private SurveyVersion(Guid id, Guid tenantId, Guid surveyId, int number, string definition, Guid createdBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        SurveyId = surveyId;
        Number = number;
        Definition = definition;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The survey.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>1, 2, 3 … in publishing order.</summary>
    public int Number { get; private set; }

    /// <summary>The definition as JSON (packages/schema format).</summary>
    public string Definition { get; private set; }

    /// <summary>Draft or published.</summary>
    public SurveyVersionState State { get; private set; }

    /// <summary>When it was published (UTC).</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Who started this version.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>When it was started (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last save (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Reads the definition.</summary>
    /// <returns>The definition.</returns>
    public SurveyDefinition ReadDefinition() => JsonSerializer.Deserialize<SurveyDefinition>(Definition, SurveyDefinitionJson.Options)!;

    /// <summary>Replaces the draft's definition (autosave).</summary>
    /// <param name="definition">The new definition.</param>
    /// <param name="now">Time of the save.</param>
    public void ReplaceDefinition(SurveyDefinition definition, DateTimeOffset now)
    {
        if (State != SurveyVersionState.Draft)
        {
            throw new InvalidOperationException("A published version never changes.");
        }

        Definition = JsonSerializer.Serialize(definition, SurveyDefinitionJson.Options);
        UpdatedAt = now;
    }

    internal static SurveyVersion NewDraft(Guid id, Survey survey, int number, SurveyDefinition definition, Guid createdBy, DateTimeOffset now) =>
        new(id, survey.TenantId, survey.Id, number, JsonSerializer.Serialize(definition, SurveyDefinitionJson.Options), createdBy, now);

    internal void MarkPublished(DateTimeOffset now)
    {
        State = SurveyVersionState.Published;
        PublishedAt = now;
        UpdatedAt = now;
    }
}
