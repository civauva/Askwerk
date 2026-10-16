// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Askwerk.Domain.Identity;

/// <summary>An organisation: owns surveys, contacts and results (agents/data-model.md, Tenancy and identity).</summary>
public sealed partial class Tenant
{
    private Tenant(Guid id, string name, string slug, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Slug = slug;
        CreatedAt = createdAt;
        EmbedOrigins = [];
    }

    /// <summary>Stable id; also the row-level security key of every tenant table.</summary>
    public Guid Id { get; private set; }

    /// <summary>Display name.</summary>
    public string Name { get; private set; }

    /// <summary>Short name used in addresses.</summary>
    public string Slug { get; private set; }

    /// <summary>When the tenant was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Websites that may show the organisation's surveys in a frame (AWK-7); none switches embedding off.</summary>
    public string[] EmbedOrigins { get; private set; }

    /// <summary>Replaces the websites that may embed the surveys.</summary>
    /// <param name="origins">Origins as browsers send them, e.g. https://www.acme.at.</param>
    public void SetEmbedOrigins(IEnumerable<string> origins) => EmbedOrigins = [.. origins];

    /// <summary>Gets the websites that may show the designer and Results in a frame (AWK-13); none switches it off.</summary>
    public string[] DesignerEmbedOrigins { get; private set; } = [];

    /// <summary>Replaces the websites that may embed the designer and Results.</summary>
    /// <param name="origins">Origins as browsers send them.</param>
    public void SetDesignerEmbedOrigins(IEnumerable<string> origins) => DesignerEmbedOrigins = [.. origins];

    /// <summary>Gets whether the organisation works (AWK-38): suspended and deleted ones are closed to their members.</summary>
    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    /// <summary>Gets when the organisation was deleted (AWK-38); its data is kept for <see cref="GracePeriod"/>, then removed (AWK-39).</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Gets when the organisation's data was removed (AWK-39); the row stays for the audit log.</summary>
    public DateTimeOffset? PurgedAt { get; private set; }

    /// <summary>How long a deleted organisation can be restored before its data is removed (AWK-39, W30).</summary>
    public static TimeSpan GracePeriod { get; } = TimeSpan.FromDays(30);

    /// <summary>Gets when the data of a deleted organisation is removed; null while it is not deleted.</summary>
    public DateTimeOffset? RemovesAt => Status == TenantStatus.Deleted && PurgedAt is null ? DeletedAt + GracePeriod : null;

    /// <summary>Renames the organisation (AWK-38).</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("name", "name-required");
        }

        Name = name.Trim();
    }

    /// <summary>Changes the short name; the caller has checked that no other organisation uses it (AWK-38).</summary>
    /// <param name="slug">The new short name.</param>
    public void ChangeSlug(string slug)
    {
        if (!IsValidSlug(slug))
        {
            throw new DomainValidationException("slug", "slug-invalid");
        }

        Slug = slug;
    }

    /// <summary>Closes the organisation to its members until resumed (AWK-38).</summary>
    public void Suspend() => Status = Status == TenantStatus.Deleted ? Status : TenantStatus.Suspended;

    /// <summary>Opens a suspended organisation again (AWK-38).</summary>
    public void Resume() => Status = Status == TenantStatus.Suspended ? TenantStatus.Active : Status;

    /// <summary>Deletes the organisation: closed for good, its data kept until removed (AWK-38).</summary>
    /// <param name="now">The time.</param>
    public void Delete(DateTimeOffset now)
    {
        Status = TenantStatus.Deleted;
        DeletedAt = now;
    }

    /// <summary>Opens a deleted organisation again, as it was, while its data is still there (AWK-39).</summary>
    /// <returns>False when it is not deleted or its data is already removed.</returns>
    public bool Restore()
    {
        if (Status != TenantStatus.Deleted || PurgedAt is not null)
        {
            return false;
        }

        Status = TenantStatus.Active;
        DeletedAt = null;

        return true;
    }

    /// <summary>
    /// Records that the organisation's data is removed (AWK-39): the short name is freed for others, the name stays for
    /// the audit log.
    /// </summary>
    /// <param name="now">The time.</param>
    public void MarkPurged(DateTimeOffset now)
    {
        Status = TenantStatus.Deleted;
        DeletedAt ??= now;
        PurgedAt = now;
        Slug = $"removed-{Id:N}"[..20];
    }

    /// <summary>Creates a tenant after validating name and slug.</summary>
    /// <param name="id">New id.</param>
    /// <param name="name">Display name.</param>
    /// <param name="slug">Short name.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The tenant.</returns>
    public static Tenant Create(Guid id, string name, string slug, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(slug);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("name", "name-required");
        }

        if (!IsValidSlug(slug))
        {
            throw new DomainValidationException("slug", "slug-invalid");
        }

        return new Tenant(id, name.Trim(), slug, now);
    }

    /// <summary>Whether a slug is usable in addresses: 2–40 of a–z, 0–9 and single inner hyphens.</summary>
    /// <param name="slug">Candidate.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValidSlug(string slug) => slug is not null && slug.Length <= 40 && SlugPattern().IsMatch(slug);

    /// <summary>A slug suggestion derived from a name: German umlauts spelled out, other accents dropped.</summary>
    /// <param name="name">The tenant name.</param>
    /// <returns>A slug, possibly invalid for very short names.</returns>
    public static string SuggestSlug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var spelled = name.ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);
        var plain = new StringBuilder();

        foreach (var character in spelled.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(character is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? character : '-');
            }
        }

        var slug = HyphenRuns().Replace(plain.ToString(), "-").Trim('-');

        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    [GeneratedRegex("-+", RegexOptions.CultureInvariant)]
    private static partial Regex HyphenRuns();
}

/// <summary>Whether an organisation works (AWK-38).</summary>
public enum TenantStatus
{
    /// <summary>Open to its members.</summary>
    Active,

    /// <summary>Closed until an installation administrator resumes it.</summary>
    Suspended,

    /// <summary>Closed for good.</summary>
    Deleted,
}
