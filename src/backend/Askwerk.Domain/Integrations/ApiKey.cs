// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Askwerk.Domain.Identity;

namespace Askwerk.Domain.Integrations;

/// <summary>The scopes an API key may carry (public-api.md); keys never manage users, roles or settings (W14).</summary>
public static class ApiScopes
{
    /// <summary>Read surveys.</summary>
    public const string SurveysRead = "surveys:read";

    /// <summary>Read responses and export them.</summary>
    public const string ResponsesRead = "responses:read";

    /// <summary>Preset "Read results": read surveys and responses, start and download exports.</summary>
    public static readonly string[] ReadResults = [SurveysRead, ResponsesRead];

    /// <summary>Preset "Full access": every scope a key may have.</summary>
    public static readonly string[] FullAccess = [SurveysRead, "surveys:write", ResponsesRead, "responses:write", "participants:read", "participants:write", "webhooks:manage"];

    /// <summary>Create, change and deactivate users through the provisioning API (AWK-12, W22); never part of a preset.</summary>
    public const string UsersManage = "users:manage";

    /// <summary>Every scope a key or OAuth client may carry: Full access and users:manage.</summary>
    public static readonly string[] Allowed = [.. FullAccess, UsersManage];
}

/// <summary>
/// An organisation's key for the Public API (AWK-25, W14 C and D). The secret is shown once; only its SHA-256 is
/// kept, which is enough for a random 44-character secret. The first 12 characters find the key.
/// </summary>
public sealed class ApiKey
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly TimeSpan UseInterval = TimeSpan.FromMinutes(1);

    private ApiKey()
    {
        Name = "";
        Prefix = "";
        SecretHash = "";
        Scopes = [];
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>What the organisation calls it, e.g. Reporting server.</summary>
    public string Name { get; private set; }

    /// <summary>The first 12 characters, to look the key up.</summary>
    public string Prefix { get; private set; }

    /// <summary>SHA-256 of the whole key, hex.</summary>
    public string SecretHash { get; private set; }

    /// <summary>What the key may do.</summary>
    public string[] Scopes { get; private set; }

    /// <summary>When it stops working (UTC); never when empty.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Revoked (UTC).</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Who created it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last call with the key (UTC).</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>Paused with its connection (W36); calls fail while paused.</summary>
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>What the list shows of the key: its first eight characters.</summary>
    public string Shown => Prefix[..8] + "…";

    /// <summary>Issues a key; the secret is returned once and never stored.</summary>
    /// <param name="id">Stable id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="name">What the organisation calls it.</param>
    /// <param name="scopes">What it may do; only scopes of <see cref="ApiScopes.Allowed"/>.</param>
    /// <param name="expiresAt">When it stops working; never when empty.</param>
    /// <param name="createdBy">Who creates it.</param>
    /// <param name="now">Now.</param>
    /// <returns>The key and its secret.</returns>
    public static (ApiKey Key, string Secret) Issue(Guid id, Guid tenantId, string name, IReadOnlyCollection<string> scopes, DateTimeOffset? expiresAt, Guid createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("name", "name-required");
        }

        if (name.Trim().Length > 100)
        {
            throw new DomainValidationException("name", "name-too-long");
        }

        if (scopes.Count == 0 || scopes.Any(scope => !ApiScopes.Allowed.Contains(scope)))
        {
            throw new DomainValidationException("scopes", "unknown-scope");
        }

        var secret = "awk_" + RandomNumberGenerator.GetString(Alphabet, 44);
        var key = new ApiKey
        {
            Id = id,
            TenantId = tenantId,
            Name = name.Trim(),
            Prefix = secret[..12],
            SecretHash = Hash(secret),
            Scopes = [.. ApiScopes.Allowed.Where(scopes.Contains)],
            ExpiresAt = expiresAt,
            CreatedBy = createdBy,
            CreatedAt = now,
        };

        return (key, secret);
    }

    /// <summary>Whether a presented secret is this key; compared in constant time.</summary>
    /// <param name="secret">The presented key.</param>
    /// <returns>True when it is.</returns>
    public bool Matches(string secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret ?? "")), Encoding.ASCII.GetBytes(SecretHash));

    /// <summary>Whether calls may use the key now.</summary>
    /// <param name="now">Now.</param>
    /// <returns>False once revoked or expired.</returns>
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && PausedAt is null && (ExpiresAt is null || now < ExpiresAt);

    /// <summary>Stops the key at once.</summary>
    /// <param name="now">Now.</param>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>Pauses the key, or lets it work again (W36).</summary>
    /// <param name="paused">Whether it is paused.</param>
    /// <param name="now">Now.</param>
    public void Pause(bool paused, DateTimeOffset now) => PausedAt = paused ? PausedAt ?? now : null;

    /// <summary>Gives the key a new secret; the old one stops working at once (W36).</summary>
    /// <returns>The new secret, shown once.</returns>
    public string NewSecret()
    {
        var secret = "awk_" + RandomNumberGenerator.GetString(Alphabet, 44);
        Prefix = secret[..12];
        SecretHash = Hash(secret);

        return secret;
    }

    /// <summary>Notes a call; the last use is written at most once a minute.</summary>
    /// <param name="now">Now.</param>
    /// <returns>True when the last use changed and should be saved.</returns>
    public bool Used(DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < UseInterval)
        {
            return false;
        }

        LastUsedAt = now;

        return true;
    }

    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
