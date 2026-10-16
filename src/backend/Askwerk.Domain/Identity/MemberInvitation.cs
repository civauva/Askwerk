// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;

namespace Askwerk.Domain.Identity;

/// <summary>
/// An invitation into an organisation (AWK-30, W18): an address, the role it gets, and a link that works once for
/// 7 days. Only the token's hash is looked up; the token itself is kept encrypted so the link can be copied again.
/// Accepted and revoked invitations are deleted; the audit log keeps them.
/// </summary>
public sealed class MemberInvitation
{
    /// <summary>How long a link works.</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromDays(7);

    // For EF Core.
    private MemberInvitation()
    {
        Email = "";
        TokenHash = "";
        TokenProtected = "";
    }

    private MemberInvitation(Guid id, Guid tenantId, string email, TenantRole role, string tokenHash, string tokenProtected, Guid invitedBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        Role = role;
        TokenHash = tokenHash;
        TokenProtected = tokenProtected;
        InvitedBy = invitedBy;
        InvitedAt = now;
        ExpiresAt = now + ValidFor;
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The address invited, trimmed and in lower case.</summary>
    public string Email { get; private set; }

    /// <summary>The role the person gets.</summary>
    public TenantRole Role { get; private set; }

    /// <summary>SHA-256 of the token, as hex; the link is looked up by it.</summary>
    public string TokenHash { get; private set; }

    /// <summary>The token, encrypted with the instance's keys, for Copy link.</summary>
    public string TokenProtected { get; private set; }

    /// <summary>Who invited.</summary>
    public Guid InvitedBy { get; private set; }

    /// <summary>When (UTC).</summary>
    public DateTimeOffset InvitedAt { get; private set; }

    /// <summary>Until when the link works (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the email went out, if it did.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>A new random token for a link: 32 bytes, URL-safe.</summary>
    /// <returns>The token.</returns>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The hash a token is looked up by.</summary>
    /// <param name="token">The token from the link.</param>
    /// <returns>64 hex characters.</returns>
    public static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    /// <summary>Normalises an address for comparison and storage.</summary>
    /// <param name="email">The address as typed.</param>
    /// <returns>Trimmed, lower case.</returns>
    public static string Normalise(string email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Invites an address.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="email">The address.</param>
    /// <param name="role">The role it gets.</param>
    /// <param name="token">The link's token.</param>
    /// <param name="tokenProtected">The token, encrypted.</param>
    /// <param name="invitedBy">Who invites.</param>
    /// <param name="now">Time of the invitation.</param>
    /// <returns>The invitation.</returns>
    public static MemberInvitation Create(Guid id, Guid tenantId, string email, TenantRole role, string token, string tokenProtected, Guid invitedBy, DateTimeOffset now)
    {
        var address = Normalise(email);

        if (address.Length is 0 or > 254 || address.IndexOf('@', StringComparison.Ordinal) is < 1 || address.EndsWith('@'))
        {
            throw new DomainValidationException("emails", "email-invalid");
        }

        return new MemberInvitation(id, tenantId, address, role, HashOf(token), tokenProtected, invitedBy, now);
    }

    /// <summary>Whether the link still works.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>True before it expires.</returns>
    public bool IsOpen(DateTimeOffset now) => now < ExpiresAt;

    /// <summary>Notes that the email went out.</summary>
    /// <param name="now">When.</param>
    public void MarkSent(DateTimeOffset now) => SentAt = now;
}
