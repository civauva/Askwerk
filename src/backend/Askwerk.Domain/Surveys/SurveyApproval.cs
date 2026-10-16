// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Askwerk.Domain.Identity;

namespace Askwerk.Domain.Surveys;

/// <summary>
/// A member's approval of a survey's draft as Preview showed it (AWK-28, W16; ISO 20252: documented approval). It is
/// pinned to the draft's fingerprint, so a later change is not approved by it. Withdrawn approvals are deleted; the audit
/// log keeps both steps.
/// </summary>
public sealed class SurveyApproval
{
    // For EF Core.
    private SurveyApproval()
    {
        Fingerprint = "";
    }

    private SurveyApproval(Guid id, Guid tenantId, Guid surveyId, int version, string fingerprint, string? note, Guid approvedBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        SurveyId = surveyId;
        Version = version;
        Fingerprint = fingerprint;
        Note = note;
        ApprovedBy = approvedBy;
        ApprovedAt = now;
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The survey.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>Number of the draft it was given for; the published version keeps it.</summary>
    public int Version { get; private set; }

    /// <summary>SHA-256 of the draft's definition, as hex.</summary>
    public string Fingerprint { get; private set; }

    /// <summary>The approver's note, if any.</summary>
    public string? Note { get; private set; }

    /// <summary>Who approved.</summary>
    public Guid ApprovedBy { get; private set; }

    /// <summary>When (UTC).</summary>
    public DateTimeOffset ApprovedAt { get; private set; }

    /// <summary>The fingerprint of a definition: SHA-256 of its JSON, as lower-case hex.</summary>
    /// <param name="definition">The draft's definition as stored.</param>
    /// <returns>64 hex characters.</returns>
    public static string FingerprintOf(string definition) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definition)));

    /// <summary>Approves the draft as it is now.</summary>
    /// <param name="id">New id.</param>
    /// <param name="draft">The survey's draft.</param>
    /// <param name="note">Optional note; trimmed, at most 500 characters.</param>
    /// <param name="approvedBy">The member.</param>
    /// <param name="now">Time of the approval.</param>
    /// <returns>The approval.</returns>
    public static SurveyApproval Approve(Guid id, SurveyVersion draft, string? note, Guid approvedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.State != SurveyVersionState.Draft)
        {
            throw new InvalidOperationException("Only the draft is approved.");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (trimmed is { Length: > 500 })
        {
            throw new DomainValidationException("note", "note-too-long");
        }

        return new SurveyApproval(id, draft.TenantId, draft.SurveyId, draft.Number, FingerprintOf(draft.Definition), trimmed, approvedBy, now);
    }
}
