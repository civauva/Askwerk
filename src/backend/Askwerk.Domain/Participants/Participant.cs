// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;

namespace Askwerk.Domain.Participants;

/// <summary>
/// A person invited to one survey (AWK-7, W12 C; data-model.md Participant): their address, their personal link's
/// token, and what happened to their invitation. Their response points back to them.
/// </summary>
public sealed class Participant
{
    // For EF Core.
    private Participant()
    {
        Email = "";
        Token = "";
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The survey.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>The invitation that added them.</summary>
    public Guid InvitationId { get; private set; }

    /// <summary>Lower case.</summary>
    public string Email { get; private set; }

    /// <summary>For {name}; may be empty.</summary>
    public string? Name { get; private set; }

    /// <summary>Their language, when known.</summary>
    public string? Language { get; private set; }

    /// <summary>The secret in their personal link (/s/{code}?t={token}) and their unsubscribe link.</summary>
    public string Token { get; private set; }

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The invitation was sent (UTC).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>They opened their link (UTC).</summary>
    public DateTimeOffset? OpenedAt { get; private set; }

    /// <summary>The mail server refused their address (UTC).</summary>
    public DateTimeOffset? BouncedAt { get; private set; }

    /// <summary>Reminders sent.</summary>
    public int Reminders { get; private set; }

    /// <summary>Last reminder (UTC).</summary>
    public DateTimeOffset? RemindedAt { get; private set; }

    /// <summary>A new participant with a fresh token.</summary>
    public static Participant Add(Guid id, Guid tenantId, Guid surveyId, Guid invitationId, string email, string? name, string? language, DateTimeOffset now) => new()
    {
        Id = id,
        TenantId = tenantId,
        SurveyId = surveyId,
        InvitationId = invitationId,
        Email = email,
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant(),
        Token = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 32),
        CreatedAt = now,
    };

    /// <summary>The invitation went out.</summary>
    public void Sent(DateTimeOffset now) => SentAt = now;

    /// <summary>The mail server refused the address; no more emails go there.</summary>
    public void Bounced(DateTimeOffset now) => BouncedAt = now;

    /// <summary>They opened their link; the first time counts.</summary>
    public void Opened(DateTimeOffset now) => OpenedAt ??= now;

    /// <summary>A reminder went out.</summary>
    public void Reminded(DateTimeOffset now)
    {
        Reminders++;
        RemindedAt = now;
    }
}

/// <summary>One sending to a list (W12 C step 2): when, the deadline, and the reminder.</summary>
public sealed class Invitation
{
    // For EF Core.
    private Invitation()
    {
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The survey.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>Emails are sent; false makes links only (email not set up).</summary>
    public bool Send { get; private set; }

    /// <summary>When the invitations go out (UTC).</summary>
    public DateTimeOffset SendAt { get; private set; }

    /// <summary>"Answer by", for {deadline}.</summary>
    public DateOnly Deadline { get; private set; }

    /// <summary>When the reminder goes out (UTC); null without one.</summary>
    public DateTimeOffset? RemindAt { get; private set; }

    /// <summary>The address for the links (a verified domain or the system address).</summary>
    public string? Host { get; private set; }

    /// <summary>Who sent it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// A new sending. The reminder goes three days before the deadline, at least a day after sending; without a
    /// deadline the invitation asks for an answer within two weeks.
    /// </summary>
    public static Invitation Create(Guid id, Guid tenantId, Guid surveyId, bool send, DateTimeOffset? sendAt, DateOnly? deadline, bool reminder, string? host, Guid createdBy, DateTimeOffset now)
    {
        var when = sendAt is { } later && later > now ? later : now;
        var by = deadline ?? DateOnly.FromDateTime(when.UtcDateTime.AddDays(14));
        var remind = new DateTimeOffset(by.AddDays(-3).ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);

        return new Invitation
        {
            Id = id,
            TenantId = tenantId,
            SurveyId = surveyId,
            Send = send,
            SendAt = when,
            Deadline = by,
            RemindAt = send && reminder ? (remind > when.AddDays(1) ? remind : when.AddDays(1)) : null,
            Host = host,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}

/// <summary>An address that asked for no more invitations from the organisation (W12 note b).</summary>
public sealed class Unsubscribe
{
    // For EF Core.
    private Unsubscribe()
    {
        Email = "";
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Lower case.</summary>
    public string Email { get; private set; }

    /// <summary>When (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records the wish.</summary>
    public static Unsubscribe Create(Guid id, Guid tenantId, string email, DateTimeOffset now) => new() { Id = id, TenantId = tenantId, Email = email, CreatedAt = now };
}
