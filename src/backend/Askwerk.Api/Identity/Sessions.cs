// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Claims;
using Askwerk.Application.Setup;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.Identity;

/// <summary>Claim types, policy names and session states of the App API's cookie session.</summary>
public static class Sessions
{
    /// <summary>Name of the session cookie.</summary>
    public const string CookieName = "askwerk.session";

    /// <summary>Claim with the tenant the session works in.</summary>
    public const string TenantClaim = "askwerk:tenant";

    /// <summary>Claim set while the user still has to set up two-factor (owners and admins).</summary>
    public const string PendingClaim = "askwerk:pending";

    /// <summary>Value of <see cref="PendingClaim"/> for the two-factor setup.</summary>
    public const string PendingTwoFactorSetup = "two-factor-setup";

    /// <summary>Policy: fully signed in, nothing pending. The default for App API endpoints.</summary>
    public const string SignedIn = "signed-in";

    /// <summary>Policy: signed in, possibly with the two-factor setup still pending; only for that setup.</summary>
    public const string SignedInPendingAllowed = "signed-in-pending-allowed";

    /// <summary>The user id of a principal.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The id, or null when not signed in.</returns>
    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The tenant of a principal's session.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The tenant, or null when none is chosen.</returns>
    public static Guid? TenantId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(TenantClaim), out var id) ? id : null;
}

/// <summary>What the client should show after a sign-in step.</summary>
/// <param name="State"><c>signed-in</c>, <c>two-factor-required</c>, <c>two-factor-setup-required</c> or <c>passkey-required</c>.</param>
public sealed record SessionResponse(string State);

/// <summary>Signs users in and decides what is still pending: owners and admins must have two-factor on.</summary>
public sealed class SessionService(SignInManager<User> signIn, AskwerkDbContext context, DbSessionContext session)
{
    /// <summary>
    /// Whether a password alone is not enough: owners and admins without the app code but with a passkey must
    /// sign in with the passkey, which is their second factor.
    /// </summary>
    /// <param name="user">The user whose password was correct.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>True when the client must ask for the passkey.</returns>
    public async Task<bool> PasskeyRequiredAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        session.UserId = user.Id;

        return !user.TwoFactorEnabled
            && await context.Passkeys.AnyAsync(passkey => passkey.UserId == user.Id, cancellationToken)
            && await context.Memberships.AnyAsync(membership => membership.UserId == user.Id && (membership.Role == TenantRole.Owner || membership.Role == TenantRole.Admin), cancellationToken);
    }

    /// <summary>Signs the user in with the right claims and returns the resulting state.</summary>
    /// <param name="user">The user, already authenticated by password (and code, if any).</param>
    /// <param name="preferredTenant">A tenant to work in, if the user is a member.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <param name="external">Signed in by a host system (W22), which answers for the second factor itself.</param>
    /// <returns>The state for the client.</returns>
    public async Task<SessionResponse> SignInAsync(User user, Guid? preferredTenant, CancellationToken cancellationToken, bool external = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        session.UserId = user.Id;
        // Deleted organisations do not count (AWK-39): with one organisation left, it opens directly.
        var memberships = await context.Memberships.AsNoTracking()
            .Where(membership => membership.UserId == user.Id && context.Tenants.Any(tenant => tenant.Id == membership.TenantId && tenant.Status != TenantStatus.Deleted))
            .ToListAsync(cancellationToken);
        var claims = new List<Claim>();
        var tenant = memberships.FirstOrDefault(membership => membership.TenantId == preferredTenant) ?? (memberships.Count == 1 ? memberships[0] : null);

        if (tenant is not null)
        {
            claims.Add(new Claim(Sessions.TenantClaim, tenant.TenantId.ToString()));
        }

        // A passkey verified with fingerprint, face or PIN counts as two-factor (Dražan, 2026-10-14).
        var setupPending = !external
            && !user.TwoFactorEnabled
            && memberships.Any(membership => membership.Role.RequiresTwoFactor())
            && !await context.Passkeys.AnyAsync(passkey => passkey.UserId == user.Id, cancellationToken);

        if (setupPending)
        {
            claims.Add(new Claim(Sessions.PendingClaim, Sessions.PendingTwoFactorSetup));
        }

        await signIn.SignInWithClaimsAsync(user, isPersistent: false, claims);
        await context.Users.Where(row => row.Id == user.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.LastSignInAt, DateTimeOffset.UtcNow), cancellationToken);

        return new SessionResponse(setupPending ? "two-factor-setup-required" : "signed-in");
    }
}

/// <summary>The accounts port of setup, backed by Identity's password validators and hasher.</summary>
public sealed class IdentityAccounts(UserManager<User> users, EfUserRepository repository) : IAccounts
{
    /// <inheritdoc />
    public async Task<string?> CreateAsync(User user, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);

            if (!result.Succeeded)
            {
                return ToCode(result.Errors.First().Code);
            }
        }

        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        repository.Add(user);

        return null;
    }

    private static string ToCode(string identityCode) => IdentityCodes.ToApiCode(identityCode);
}

/// <summary>Maps Identity's error codes to the API's codes.</summary>
public static class IdentityCodes
{
    /// <summary>Identity's codes (PasswordTooShort) as the API's kebab-case codes (password-too-short).</summary>
    /// <param name="identityCode">Identity's code.</param>
    /// <returns>The API code.</returns>
    public static string ToApiCode(string identityCode) =>
        string.Concat((identityCode ?? "").Select((character, index) => char.IsUpper(character) && index > 0 ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}
