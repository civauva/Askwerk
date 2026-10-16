// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;

namespace Askwerk.Domain.Identity;

/// <summary>
/// A session of the designer or Results inside a host product (AWK-13, W21): started from a hand-over token, held by
/// the frame as a bearer token in memory (no third-party cookie), for one survey. It lasts an hour, is renewed while the
/// frame is open, and ends after 8 hours at most.
/// </summary>
public sealed class EmbedSession
{
    /// <summary>How long a token works without renewal.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>How long a session lasts at most.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(8);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private EmbedSession()
    {
        TokenHash = "";
        View = "";
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the one survey it may open.</summary>
    public Guid SurveyId { get; private set; }

    /// <summary>Gets what it shows: designer or results.</summary>
    public string View { get; private set; }

    /// <summary>Gets the SHA-256 of the token, as hex.</summary>
    public string TokenHash { get; private set; }

    /// <summary>Gets when the token stops working unless renewed.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when the session ends for good.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Gets when it started.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Starts a session; the token (aws_…) is returned once and only its hash is kept.</summary>
    public static (EmbedSession Session, string Token) Start(Guid id, Guid tenantId, Guid userId, Guid surveyId, string view, DateTimeOffset now)
    {
        var token = "aws_" + RandomNumberGenerator.GetString(Alphabet, 40);
        var ends = now + MaxLength;

        return (new EmbedSession { Id = id, TenantId = tenantId, UserId = userId, SurveyId = surveyId, View = view, TokenHash = HashOf(token), ExpiresAt = Min(now + Window, ends), EndsAt = ends, CreatedAt = now }, token);
    }

    /// <summary>The stored hash of a token.</summary>
    public static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    /// <summary>Renews the token for another hour, never past the end.</summary>
    public void Renew(DateTimeOffset now) => ExpiresAt = Min(now + Window, EndsAt);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
