// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Surveys;

/// <summary>
/// A survey definition the organisation saved for reuse (AWK-17, W10 C). A new survey from it gets a copy, so later
/// changes to either never reach the other.
/// </summary>
public sealed class SurveyTemplate
{
    private SurveyTemplate()
    {
        Name = "";
        Definition = "{}";
    }

    private SurveyTemplate(Guid id, Guid tenantId, string name, string definition, Guid createdBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Definition = definition;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the name shown when choosing a template.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the definition as JSON.</summary>
    public string Definition { get; private set; }

    /// <summary>Gets who saved it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Gets when it was saved.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the survey the template is edited in (W32): the designer opens it like any survey, but it is never listed,
    /// published or answered. Null until the template is first edited; then its draft and look are the template's.
    /// </summary>
    public Guid? SurveyId { get; private set; }

    /// <summary>Edits the template in a survey from now on (W32).</summary>
    /// <param name="surveyId">The survey holding the template's definition and look.</param>
    public void EditIn(Guid surveyId) => SurveyId = surveyId;

    /// <summary>Follows the editing survey's title (W32).</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        Name = trimmed[..Math.Min(trimmed.Length, 200)];
    }

    /// <summary>A template from a survey's draft.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="name">Its name.</param>
    /// <param name="definition">The definition as JSON.</param>
    /// <param name="createdBy">The user.</param>
    /// <param name="now">The time.</param>
    /// <returns>The template.</returns>
    public static SurveyTemplate Create(Guid id, Guid tenantId, string name, string definition, Guid createdBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition);
        var trimmed = name.Trim();

        return new SurveyTemplate(id, tenantId, trimmed[..Math.Min(trimmed.Length, 200)], definition, createdBy, now);
    }
}
