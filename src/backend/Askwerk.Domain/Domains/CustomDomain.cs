// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Askwerk.Domain.Domains;

/// <summary>Where a custom domain stands.</summary>
public enum DomainStatus
{
    /// <summary>Added; waiting for its DNS records.</summary>
    Pending,

    /// <summary>Both records found: links, QR codes and certificates use it.</summary>
    Verified,

    /// <summary>Not found within seven days; Verify again starts over.</summary>
    Failed,
}

/// <summary>
/// An organisation's own domain for survey links (AWK-7, W12 A; data-model.md Domain). It is verified by two DNS
/// records: a CNAME to the instance's address, so respondents arrive here, and a TXT record with a token, so only the
/// domain's owner can claim it. At most one domain of an organisation is primary; without one, the system address is.
/// </summary>
public sealed partial class CustomDomain
{
    /// <summary>How long Askwerk keeps checking a new domain by itself.</summary>
    public static readonly TimeSpan CheckFor = TimeSpan.FromDays(7);

    // For EF Core.
    private CustomDomain()
    {
        Hostname = "";
        VerificationToken = "";
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Lower case, without a trailing dot; unique across the instance.</summary>
    public string Hostname { get; private set; }

    /// <summary>The secret the TXT record must carry.</summary>
    public string VerificationToken { get; private set; }

    /// <summary>Pending, verified or failed.</summary>
    public DomainStatus Status { get; private set; }

    /// <summary>The organisation's address for links, QR codes, the PDF and invitations.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>The CNAME pointed to the instance at the last check.</summary>
    public bool CnameFound { get; private set; }

    /// <summary>The TXT record carried the token at the last check.</summary>
    public bool TxtFound { get; private set; }

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last DNS check (UTC).</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>Verified (UTC).</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>The name of the TXT record to set.</summary>
    public string TxtName => $"_askwerk.{Hostname}";

    /// <summary>The value of the TXT record to set.</summary>
    public string TxtValue => $"askwerk-verify={VerificationToken}";

    /// <summary>Adds a domain, waiting for its records.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="hostname">A hostname from <see cref="Normalize"/>.</param>
    /// <param name="now">When.</param>
    /// <returns>The domain.</returns>
    public static CustomDomain Add(Guid id, Guid tenantId, string hostname, DateTimeOffset now) => new()
    {
        Id = id,
        TenantId = tenantId,
        Hostname = hostname,
        VerificationToken = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 32),
        CreatedAt = now,
    };

    /// <summary>A hostname as typed, made comparable, or why it cannot be one.</summary>
    /// <param name="input">What was typed, e.g. " Surveys.Acme.at. ".</param>
    /// <param name="systemHost">The instance's own host, which cannot be added.</param>
    /// <param name="hostname">Lower case, without a trailing dot.</param>
    /// <returns>Null when fine; else hostname-invalid, hostname-not-subdomain or hostname-is-system.</returns>
    public static string? Normalize(string? input, string systemHost, out string hostname)
    {
        hostname = (input ?? "").Trim().TrimEnd('.').ToLowerInvariant();

        if (hostname.Length > 253 || !HostnamePattern().IsMatch(hostname))
        {
            return "hostname-invalid";
        }

        if (hostname.Count(character => character == '.') < 2)
        {
            return "hostname-not-subdomain";
        }

        return string.Equals(hostname, systemHost, StringComparison.OrdinalIgnoreCase) ? "hostname-is-system" : null;
    }

    /// <summary>Records what DNS showed; both records verify the domain.</summary>
    /// <param name="cnameFound">The CNAME points to the instance.</param>
    /// <param name="txtFound">The TXT record carries the token.</param>
    /// <param name="now">When.</param>
    /// <returns>Whether the domain was verified by this check.</returns>
    public bool Checked(bool cnameFound, bool txtFound, DateTimeOffset now)
    {
        CnameFound = cnameFound;
        TxtFound = txtFound;
        CheckedAt = now;

        if (Status == DomainStatus.Verified || !cnameFound || !txtFound)
        {
            if (Status == DomainStatus.Failed)
            {
                // Verify again: Askwerk keeps checking for another seven days.
                Status = DomainStatus.Pending;
                CreatedAt = now;
            }

            return false;
        }

        Status = DomainStatus.Verified;
        VerifiedAt = now;

        return true;
    }

    /// <summary>Stops checking a domain whose records did not appear in time.</summary>
    /// <param name="now">When.</param>
    /// <returns>Whether it failed now.</returns>
    public bool GiveUpIfLate(DateTimeOffset now)
    {
        if (Status != DomainStatus.Pending || now - CreatedAt < CheckFor)
        {
            return false;
        }

        Status = DomainStatus.Failed;

        return true;
    }

    /// <summary>Makes this the organisation's primary domain, or not.</summary>
    /// <param name="primary">Primary or not.</param>
    public void SetPrimary(bool primary)
    {
        if (primary && Status != DomainStatus.Verified)
        {
            throw new InvalidOperationException("Only a verified domain can be primary.");
        }

        IsPrimary = primary;
    }

    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnamePattern();
}
