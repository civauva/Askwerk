// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.Identity;

/// <summary>
/// ASP.NET Core Identity's view of <see cref="User"/>: our own table, Identity's password hashing, lockout,
/// two-factor and recovery codes (coding-standards.md: never hand-rolled). The email is the user name.
/// </summary>
public sealed class UserStore(AskwerkDbContext context, IDataProtectionProvider protection, TimeProvider time, SecurityStamps stamps) :
    IUserEmailStore<User>,
    IUserPasswordStore<User>,
    IUserSecurityStampStore<User>,
    IUserLockoutStore<User>,
    IUserTwoFactorStore<User>,
    IUserAuthenticatorKeyStore<User>,
    IUserTwoFactorRecoveryCodeStore<User>,
    IUserPasskeyStore<User>
{
    // The authenticator key is a secret that must survive restarts: encrypted with the instance's data-protection keys.
    private readonly IDataProtector _keyProtector = protection.CreateProtector("askwerk.identity.authenticator-key.v1");

    /// <inheritdoc />
    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) => Task.FromResult(Check(user).Id.ToString());

    /// <inheritdoc />
    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(Check(user).Email);

    /// <inheritdoc />
    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The user name is the email; changing the email is its own flow.");

    /// <inheritdoc />
    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(Check(user).NormalizedEmail);

    /// <inheritdoc />
    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        context.Users.Add(Check(user));
        await context.SaveChangesAsync(cancellationToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        Check(user);
        await context.SaveChangesAsync(cancellationToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Users are deactivated, not deleted (data-model.md).");

    /// <inheritdoc />
    public Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id) ? context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken) : Task.FromResult<User?>(null);

    /// <inheritdoc />
    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => FindByEmailAsync(normalizedUserName, cancellationToken);

    /// <inheritdoc />
    public Task SetEmailAsync(User user, string? email, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Changing the email is its own flow.");

    /// <inheritdoc />
    public Task<string?> GetEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(Check(user).Email);

    /// <inheritdoc />
    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail && user.DeactivatedAt == null, cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(Check(user).NormalizedEmail);

    /// <inheritdoc />
    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        Check(user).PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) => Task.FromResult(Check(user).PasswordHash);

    /// <inheritdoc />
    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) => Task.FromResult(Check(user).PasswordHash is not null);

    /// <inheritdoc />
    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        Check(user).SecurityStamp = stamp;

        // Other sessions of this user end on their next request (AWK-21).
        stamps.Remember(user.Id, stamp);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(Check(user).SecurityStamp);

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken) => Task.FromResult(Check(user).LockoutEnd);

    /// <inheritdoc />
    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        Check(user).LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken cancellationToken) => Task.FromResult(++Check(user).AccessFailedCount);

    /// <inheritdoc />
    public Task ResetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        Check(user).AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken) => Task.FromResult(Check(user).AccessFailedCount);

    /// <inheritdoc />
    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetTwoFactorEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        Check(user).TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> GetTwoFactorEnabledAsync(User user, CancellationToken cancellationToken) => Task.FromResult(Check(user).TwoFactorEnabled);

    /// <inheritdoc />
    public Task SetAuthenticatorKeyAsync(User user, string key, CancellationToken cancellationToken)
    {
        Check(user).AuthenticatorKey = _keyProtector.Protect(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(Check(user).AuthenticatorKey is { } stored ? _keyProtector.Unprotect(stored) : null);

    /// <inheritdoc />
    public Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        // Like passwords, recovery codes are kept only as hashes; they are shown to the user once.
        Check(user).RecoveryCodes = string.Join(';', recoveryCodes.Select(HashCode));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        var codes = (Check(user).RecoveryCodes ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (!codes.Remove(HashCode(code)))
        {
            return Task.FromResult(false);
        }

        user.RecoveryCodes = string.Join(';', codes);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int> CountCodesAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult((Check(user).RecoveryCodes ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Length);

    /// <inheritdoc />
    /// <remarks>Identity calls this after registration and after every sign-in with the passkey (new signature counter).</remarks>
    public async Task AddOrUpdatePasskeyAsync(User user, UserPasskeyInfo passkey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passkey);
        var existing = await context.Passkeys.FirstOrDefaultAsync(row => row.CredentialId == passkey.CredentialId, cancellationToken);

        if (existing is null)
        {
            existing = new UserPasskey { CredentialId = passkey.CredentialId, UserId = Check(user).Id, PublicKey = passkey.PublicKey, CreatedAt = passkey.CreatedAt };
            context.Passkeys.Add(existing);
        }
        else
        {
            existing.LastUsedAt = time.GetUtcNow();
        }

        existing.PublicKey = passkey.PublicKey;
        existing.SignCount = passkey.SignCount;
        existing.Transports = passkey.Transports ?? [];
        existing.IsUserVerified = passkey.IsUserVerified;
        existing.IsBackupEligible = passkey.IsBackupEligible;
        existing.IsBackedUp = passkey.IsBackedUp;
        existing.AttestationObject = passkey.AttestationObject;
        existing.ClientDataJson = passkey.ClientDataJson;
        existing.Name = passkey.Name ?? existing.Name;
    }

    /// <inheritdoc />
    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(User user, CancellationToken cancellationToken)
    {
        var userId = Check(user).Id;
        var rows = await context.Passkeys.Where(row => row.UserId == userId).ToListAsync(cancellationToken);

        return [.. rows.Select(ToInfo)];
    }

    /// <inheritdoc />
    public async Task<User?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        var passkey = await context.Passkeys.AsNoTracking().FirstOrDefaultAsync(row => row.CredentialId == credentialId, cancellationToken);

        return passkey is null ? null : await context.Users.FirstOrDefaultAsync(row => row.Id == passkey.UserId && row.DeactivatedAt == null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserPasskeyInfo?> FindPasskeyAsync(User user, byte[] credentialId, CancellationToken cancellationToken)
    {
        var userId = Check(user).Id;
        var passkey = await context.Passkeys.FirstOrDefaultAsync(row => row.UserId == userId && row.CredentialId == credentialId, cancellationToken);

        return passkey is null ? null : ToInfo(passkey);
    }

    /// <inheritdoc />
    public async Task RemovePasskeyAsync(User user, byte[] credentialId, CancellationToken cancellationToken)
    {
        var userId = Check(user).Id;
        var passkey = await context.Passkeys.FirstOrDefaultAsync(row => row.UserId == userId && row.CredentialId == credentialId, cancellationToken);

        if (passkey is not null)
        {
            context.Passkeys.Remove(passkey);
        }
    }

    private static UserPasskeyInfo ToInfo(UserPasskey row) =>
        new(row.CredentialId, row.PublicKey, row.CreatedAt, (uint)row.SignCount, row.Transports, row.IsUserVerified, row.IsBackupEligible, row.IsBackedUp, row.AttestationObject, row.ClientDataJson)
        {
            Name = row.Name,
        };

    /// <inheritdoc />
    public void Dispose()
    {
        // The context belongs to the request scope.
    }

    private static string HashCode(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));

    private static User Check(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user;
    }
}
