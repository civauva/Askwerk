// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.Identity;

/// <summary>
/// A hand-over token (AWK-12, W22): a JWT the host product signs with RS256 or ES256, checked with .NET's own crypto.
/// iss, aud (askwerk:{organisation}), sub, email, exp at most 5 minutes ahead and jti are required; name, groups or
/// roles, and return (a path in Askwerk) are optional.
/// </summary>
public sealed record HandoverClaims(Guid TenantId, string Subject, string Email, string Name, IReadOnlyList<string> Groups, string Jti, DateTimeOffset ExpiresAt, string? ReturnTo)
{
    /// <summary>Gets the survey an embedded session or an identified respondent is for (W21), from the claim survey.</summary>
    public Guid? Survey { get; init; }

    /// <summary>Gets the host's language (claim locale), which wins over the browser's.</summary>
    public string? Locale { get; init; }
}

/// <summary>Reads and checks hand-over tokens.</summary>
public static class HandoverTokens
{
    /// <summary>How far ahead exp may be.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);
    private static readonly string[] GroupClaims = ["groups", "roles"];

    /// <summary>The organisation the token names in aud, read before the signature is checked (to find its keys).</summary>
    public static Guid? TenantOf(string token)
    {
        try
        {
            var payload = Part(token, 1);

            return payload.TryGetProperty("aud", out var aud) ? Audiences(aud).Select(TenantSignIn.TenantOfAudience).FirstOrDefault(tenant => tenant is not null) : null;
        }
        catch (Exception error) when (error is FormatException or JsonException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Checks signature, issuer, audience, expiry and the required claims; null when anything fails.</summary>
    public static HandoverClaims? Check(string token, Guid tenantId, string issuer, IReadOnlyList<AsymmetricAlgorithm> keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(keys);

        try
        {
            var parts = token.Split('.');

            if (parts.Length != 3)
            {
                return null;
            }

            var header = Part(token, 0);
            var payload = Part(token, 1);
            var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var signature = Base64Url(parts[2]);
            var algorithm = header.TryGetProperty("alg", out var alg) ? alg.GetString() : null;
            var verified = algorithm switch
            {
                "RS256" => keys.OfType<RSA>().Any(key => key.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
                "ES256" => keys.OfType<ECDsa>().Any(key => key.VerifyData(signed, signature, HashAlgorithmName.SHA256)),
                _ => false,
            };

            if (!verified || Text(payload, "iss") != issuer || !payload.TryGetProperty("aud", out var aud) || !Audiences(aud).Contains(TenantSignIn.AudienceOf(tenantId)))
            {
                return null;
            }

            if (!payload.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var expires = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());

            if (expires < now - Skew || expires > now + MaxLifetime + Skew)
            {
                return null;
            }

            if (payload.TryGetProperty("nbf", out var nbf) && nbf.ValueKind == JsonValueKind.Number && DateTimeOffset.FromUnixTimeSeconds(nbf.GetInt64()) > now + Skew)
            {
                return null;
            }

            var subject = Text(payload, "sub");
            var email = Text(payload, "email");
            var jti = Text(payload, "jti");

            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(jti) || jti.Length > 200)
            {
                return null;
            }

            var groups = GroupClaims
                .Where(name => payload.TryGetProperty(name, out _))
                .SelectMany(name => Audiences(payload.GetProperty(name)))
                .ToList();
            var returnTo = Text(payload, "return");

            return new HandoverClaims(
                tenantId,
                subject,
                email,
                Text(payload, "name") is { Length: > 0 } name ? name : email,
                groups,
                jti,
                expires,
                returnTo is not null && returnTo.StartsWith('/') && !returnTo.StartsWith("//", StringComparison.Ordinal) && !returnTo.Contains('\\', StringComparison.Ordinal) ? returnTo : null)
            {
                Survey = Guid.TryParse(Text(payload, "survey"), out var survey) ? survey : null,
                Locale = Text(payload, "locale") is { Length: > 0 and <= 10 } locale ? locale : null,
            };
        }
        catch (Exception error) when (error is FormatException or JsonException or CryptographicException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A public key from PEM (RSA or EC); null when it is neither.</summary>
    public static AsymmetricAlgorithm? KeyFromPem(string pem)
    {
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);

            return rsa;
        }
        catch (Exception error) when (error is ArgumentException or CryptographicException)
        {
            // not RSA; try EC
        }

        try
        {
            var ec = ECDsa.Create();
            ec.ImportFromPem(pem);

            return ec;
        }
        catch (Exception error) when (error is ArgumentException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>The RSA and P-256 keys of a JWKS document.</summary>
    public static IReadOnlyList<AsymmetricAlgorithm> KeysFromJwks(string json)
    {
        var keys = new List<AsymmetricAlgorithm>();
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("keys", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return keys;
        }

        foreach (var key in list.EnumerateArray())
        {
            switch (Text(key, "kty"))
            {
                case "RSA" when Text(key, "n") is { } n && Text(key, "e") is { } e:
                    keys.Add(RSA.Create(new RSAParameters { Modulus = Base64Url(n), Exponent = Base64Url(e) }));
                    break;
                case "EC" when Text(key, "crv") == "P-256" && Text(key, "x") is { } x && Text(key, "y") is { } y:
                    keys.Add(ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = Base64Url(x), Y = Base64Url(y) } }));
                    break;
            }
        }

        return keys;
    }

    private static JsonElement Part(string token, int index) => JsonDocument.Parse(Base64Url(token.Split('.')[index])).RootElement.Clone();

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<string> Audiences(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString()!],
            JsonValueKind.Array => value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!),
            _ => [],
        };

    private static byte[] Base64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }
}

/// <summary>The keys of an organisation's hand-over: its PEM keys and, cached 10 minutes, those of its JWKS URL.</summary>
public sealed class HandoverKeys(IHttpClientFactory clients, TimeProvider time)
{
    /// <summary>The HttpClient name for JWKS requests.</summary>
    public const string Client = "handover-jwks";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, IReadOnlyList<AsymmetricAlgorithm> Keys)> _jwks = new(StringComparer.Ordinal);

    /// <summary>Every key that may have signed a token of this organisation.</summary>
    public async Task<IReadOnlyList<AsymmetricAlgorithm>> OfAsync(TenantSignIn settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var keys = settings.HandoverKeys.Select(HandoverTokens.KeyFromPem).OfType<AsymmetricAlgorithm>().ToList();

        if (settings.HandoverJwksUrl is { } url)
        {
            if (!_jwks.TryGetValue(url, out var cached) || cached.Until < time.GetUtcNow())
            {
                try
                {
                    var json = await clients.CreateClient(Client).GetStringAsync(new Uri(url), cancellationToken);
                    cached = (time.GetUtcNow() + CacheFor, HandoverTokens.KeysFromJwks(json));
                    _jwks[url] = cached;
                }
                catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
                {
                    cached = (DateTimeOffset.MinValue, []);
                }
            }

            keys.AddRange(cached.Keys);
        }

        return keys;
    }
}

/// <summary>What happened to a host user: the Askwerk user and whether the user or membership was new.</summary>
public sealed record ExternalUserResult(User User, ExternalIdentity Identity, bool Created, TenantRole Role);

/// <summary>
/// Creates or updates a host user in an organisation, for the hand-over and the provisioning API alike (W22): found
/// by the host's id, else matched once by email, else created without a password; the membership gets the role.
/// Owners are never changed by a host. The caller saves with the unit of work.
/// </summary>
public sealed class ExternalUsers(AskwerkDbContext context, IAuditWriter audit, TimeProvider time, ITraceContext trace)
{
    /// <summary>Applies a host user; the actor is written to the audit log.</summary>
    public async Task<ExternalUserResult> ApplyAsync(Guid tenantId, string externalId, string email, string name, TenantRole role, string source, (AuditActorKind Kind, string Id)? actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var identity = await context.ExternalIdentities.FirstOrDefaultAsync(row => row.TenantId == tenantId && row.ExternalId == externalId, cancellationToken);
        User? user;
        var created = false;

        if (identity is not null)
        {
            user = await context.Users.FirstAsync(row => row.Id == identity.UserId, cancellationToken);
            user.UpdateFromHost(email, name);
            identity.SetActive(true, source, now);
        }
        else
        {
            var normalized = email.Trim().ToUpperInvariant();
            user = await context.Users.FirstOrDefaultAsync(row => row.NormalizedEmail == normalized, cancellationToken);

            if (user is null)
            {
                user = User.CreateExternal(Guid.CreateVersion7(now), email, name, now);
                context.Users.Add(user);
                created = true;
            }

            identity = ExternalIdentity.Create(Guid.CreateVersion7(now), tenantId, user.Id, externalId, source, now);
            context.ExternalIdentities.Add(identity);
        }

        var by = actor ?? (AuditActorKind.User, user.Id.ToString());

        if (created)
        {
            await AuditAsync(tenantId, by, "user.provisioned", "User", user.Id, new { externalId, email, source }, cancellationToken);
        }

        var membership = await context.Memberships.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == user.Id, cancellationToken);

        if (membership is null)
        {
            var added = new Membership(Guid.CreateVersion7(now), user.Id, tenantId, null, role, now);
            context.Memberships.Add(added);
            await AuditAsync(tenantId, by, "member.joined", "Membership", added.Id, new { role = Name(role), email, source }, cancellationToken);
        }
        else if (membership.Role != TenantRole.Owner && membership.Role != role)
        {
            await context.Memberships.Where(row => row.Id == membership.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Role, role), cancellationToken);
            await AuditAsync(tenantId, by, "member.role-changed", "Membership", membership.Id, new { user = user.Id, from = Name(membership.Role), to = Name(role), source }, cancellationToken);
        }

        return new ExternalUserResult(user, identity, created, membership?.Role == TenantRole.Owner ? TenantRole.Owner : role);
    }

    /// <summary>Deactivates a host user: out of the organisation, sessions ended; the user and their responses stay.</summary>
    public async Task DeactivateAsync(ExternalIdentity identity, (AuditActorKind Kind, string Id) actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.SetActive(false, "provisioning", time.GetUtcNow());
        var removed = await context.Memberships.Where(row => row.TenantId == identity.TenantId && row.UserId == identity.UserId && row.Role != TenantRole.Owner).ExecuteDeleteAsync(cancellationToken);
        var user = await context.Users.FirstAsync(row => row.Id == identity.UserId, cancellationToken);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await AuditAsync(identity.TenantId, actor, "user.deactivated", "User", identity.UserId, new { externalId = identity.ExternalId, removed = removed > 0 }, cancellationToken);
    }

    private static string Name(TenantRole role) => role.ToString().ToLowerInvariant();

    private Task AuditAsync(Guid tenantId, (AuditActorKind Kind, string Id) actor, string action, string entityType, Guid entityId, object diff, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditContent(time.GetUtcNow(), tenantId, actor.Kind, actor.Id, action, entityType, entityId.ToString(), JsonSerializer.Serialize(diff), trace.CurrentTraceId), cancellationToken);
}

/// <summary>
/// The hand-over (AWK-12, W22): a host product sends its signed-in user here with a token, as a link
/// (GET /handover?token=…) or a form post (POST /handover, field token). Askwerk checks it, creates or updates the
/// user and membership, starts the session and opens Askwerk (or the token's return path).
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("handover")]
public sealed class HandoverController(
    AskwerkDbContext context,
    DbSessionContext session,
    HandoverKeys keys,
    ExternalUsers externalUsers,
    SessionService sessions,
    IUnitOfWork unitOfWork,
    TimeProvider time) : ControllerBase
{
    /// <summary>A hand-over as a link.</summary>
    [HttpGet]
    public Task<IActionResult> Link([FromQuery] string? token, CancellationToken cancellationToken) => HandOverAsync(token, cancellationToken);

    /// <summary>A hand-over as a form post.</summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public Task<IActionResult> Post([FromForm] string? token, CancellationToken cancellationToken) => HandOverAsync(token, cancellationToken);

    private async Task<IActionResult> HandOverAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_000 || HandoverTokens.TenantOf(token) is not { } tenantId)
        {
            return Failed("invalid");
        }

        session.TenantId = tenantId;
        await context.Database.CloseConnectionAsync();
        var settings = await context.TenantSignIns.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);

        if (settings is not { HandoverEnabled: true, HandoverIssuer: { } issuer })
        {
            return Failed("invalid");
        }

        var now = time.GetUtcNow();

        if (HandoverTokens.Check(token, tenantId, issuer, await keys.OfAsync(settings, cancellationToken), now) is not { } claims)
        {
            return Failed("invalid");
        }

        await context.HandoverTokenUses.Where(row => row.TenantId == tenantId && row.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        if (await context.HandoverTokenUses.AnyAsync(row => row.TenantId == tenantId && row.Jti == claims.Jti, cancellationToken))
        {
            return Failed("invalid");
        }

        context.HandoverTokenUses.Add(new HandoverTokenUse(tenantId, claims.Jti, claims.ExpiresAt));
        var identity = await context.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId && row.ExternalId == claims.Subject, cancellationToken);

        if (identity is { Active: false } || settings.RoleFor(claims.Groups) is not { } role)
        {
            await context.SaveChangesAsync(cancellationToken);
            return Failed("no-access", await context.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.Name).FirstOrDefaultAsync(cancellationToken));
        }

        ExternalUserResult result;

        try
        {
            result = await externalUsers.ApplyAsync(tenantId, claims.Subject, claims.Email, claims.Name, role, "handover", null, cancellationToken);
        }
        catch (DomainValidationException)
        {
            return Failed("invalid");
        }

        session.UserId = result.User.Id;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await sessions.SignInAsync(result.User, tenantId, cancellationToken, external: true);

        return Redirect(SignInFailure.SafeReturn(claims.ReturnTo));
    }

    private ContentResult Failed(string reason, string? organisation = null) => SignInFailure.Result(Request, reason, organisation);
}
