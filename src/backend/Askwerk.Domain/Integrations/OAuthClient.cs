// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Askwerk.Domain.Identity;

namespace Askwerk.Domain.Integrations;

/// <summary>
/// An OAuth 2.0 client of an organisation (AWK-32, C32 B): a client id and a secret shown once, with the same scopes
/// and expiry as keys. It exchanges its credentials for short-lived access tokens; revoking it ends them at once.
/// </summary>
public sealed class OAuthClient
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // For EF Core.
    private OAuthClient()
    {
        Name = "";
        ClientId = "";
        SecretHash = "";
        Scopes = [];
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets what the organisation calls it.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the public client id (cl_…).</summary>
    public string ClientId { get; private set; }

    /// <summary>Gets the SHA-256 of the secret, as hex.</summary>
    public string SecretHash { get; private set; }

    /// <summary>Gets the scopes its tokens may have.</summary>
    public string[] Scopes { get; private set; }

    /// <summary>Gets when it stops working; never when null.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets when it was revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Gets who created it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when it last got a token.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>Creates a client and its secret (acs_…), which is shown only once.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="scopes">Its scopes.</param>
    /// <param name="expiresAt">When it stops working.</param>
    /// <param name="createdBy">Who creates it.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The client and its secret.</returns>
    public static (OAuthClient Client, string Secret) Issue(Guid id, Guid tenantId, string name, IReadOnlyCollection<string> scopes, DateTimeOffset? expiresAt, Guid createdBy, DateTimeOffset now)
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

        var secret = "acs_" + RandomNumberGenerator.GetString(Alphabet, 44);
        var client = new OAuthClient
        {
            Id = id,
            TenantId = tenantId,
            Name = name.Trim(),
            ClientId = "cl_" + RandomNumberGenerator.GetString(Alphabet, 21),
            SecretHash = Hashes.Of(secret),
            Scopes = [.. ApiScopes.Allowed.Where(scopes.Contains)],
            ExpiresAt = expiresAt,
            CreatedBy = createdBy,
            CreatedAt = now,
        };

        return (client, secret);
    }

    /// <summary>Whether a secret is this client's, in constant time.</summary>
    /// <param name="secret">The secret given.</param>
    /// <returns>True when it is.</returns>
    public bool Matches(string? secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hashes.Of(secret ?? "")), Encoding.ASCII.GetBytes(SecretHash));

    /// <summary>Whether it may get tokens now.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>True when neither revoked nor expired.</returns>
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || now < ExpiresAt);

    /// <summary>Revokes it; its tokens stop working at once.</summary>
    /// <param name="now">When.</param>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>An access token of an OAuth client (C32 B): opaque, stored as its hash, 15 minutes long.</summary>
public sealed class AccessToken
{
    /// <summary>How long a token works.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // For EF Core.
    private AccessToken()
    {
        TokenHash = "";
        Scopes = [];
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the client it was given to.</summary>
    public Guid ClientId { get; private set; }

    /// <summary>Gets the SHA-256 of the token, as hex.</summary>
    public string TokenHash { get; private set; }

    /// <summary>Gets its scopes.</summary>
    public string[] Scopes { get; private set; }

    /// <summary>Gets when it stops working.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when it was given (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gives a client a token (awt_…) with some of its scopes.</summary>
    /// <param name="id">New id.</param>
    /// <param name="client">The client.</param>
    /// <param name="scopes">The scopes, a subset of the client's.</param>
    /// <param name="now">When.</param>
    /// <returns>The token and its value.</returns>
    public static (AccessToken Token, string Value) Issue(Guid id, OAuthClient client, IEnumerable<string> scopes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);
        var value = "awt_" + RandomNumberGenerator.GetString(Alphabet, 44);

        return (new AccessToken { Id = id, TenantId = client.TenantId, ClientId = client.Id, TokenHash = Hashes.Of(value), Scopes = [.. scopes], ExpiresAt = now + Lifetime, CreatedAt = now }, value);
    }

    /// <summary>The hash a token is looked up by.</summary>
    /// <param name="value">The token.</param>
    /// <returns>64 hex characters.</returns>
    public static string HashOf(string value) => Hashes.Of(value);
}

internal static class Hashes
{
    internal static string Of(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
