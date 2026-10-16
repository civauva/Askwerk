// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Identity;

/// <summary>Where an account comes from.</summary>
public enum UserSource
{
    /// <summary>Created in Askwerk (setup, invitation).</summary>
    Local = 0,

    /// <summary>Created by a host system through the provisioning API (AWK-12).</summary>
    Provisioned = 1,
}

/// <summary>One account per person across the installation (agents/data-model.md). Credentials are handled by ASP.NET Core Identity.</summary>
public sealed class User
{
    private User(Guid id, string email, string displayName, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        NormalizedEmail = email.ToUpperInvariant();
        DisplayName = displayName;
        CreatedAt = createdAt;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Email as entered.</summary>
    public string Email { get; private set; }

    /// <summary>Upper-case email for lookups.</summary>
    public string NormalizedEmail { get; private set; }

    /// <summary>Name shown in the app.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Password hash from Identity's hasher; null for users who only sign in externally.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Changes whenever credentials change; invalidates other sessions.</summary>
    public string SecurityStamp { get; set; }

    /// <summary>Whether two-factor sign-in is on.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Authenticator (TOTP) key, protected by the data key at rest.</summary>
    public string? AuthenticatorKey { get; set; }

    /// <summary>Hashed recovery codes, separated by ';'.</summary>
    public string? RecoveryCodes { get; set; }

    /// <summary>Failed sign-ins since the last success.</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>Signed in again only after this time.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Preferred UI language (de or en); null follows the browser.</summary>
    public string? Language { get; set; }

    /// <summary>Preferred theme: system, light or dark.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Where the account comes from.</summary>
    public UserSource Source { get; private set; }

    /// <summary>When the account was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the account was deactivated; it cannot sign in.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>Gets or sets when the user last started a session (UTC); shown on Members (AWK-30).</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Gets whether the user administers this Askwerk (AWK-38): every organisation, the administrators, the installation.</summary>
    public bool IsInstallationAdmin { get; private set; }

    /// <summary>Makes the user an installation administrator, or not (AWK-38).</summary>
    /// <param name="on">Whether the user is one.</param>
    public void SetInstallationAdmin(bool on) => IsInstallationAdmin = on;

    /// <summary>Creates a user that a host system signs in or provisions (W22): no Askwerk password.</summary>
    /// <param name="id">New id.</param>
    /// <param name="email">Email address.</param>
    /// <param name="displayName">Name.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The user.</returns>
    public static User CreateExternal(Guid id, string email, string displayName, DateTimeOffset now)
    {
        var user = Create(id, email, displayName, now);
        user.Source = UserSource.Provisioned;

        return user;
    }

    /// <summary>Takes the host's name and address for a user the host manages; local accounts keep their own.</summary>
    /// <param name="email">Email address.</param>
    /// <param name="displayName">Name.</param>
    public void UpdateFromHost(string email, string displayName)
    {
        if (Source != UserSource.Provisioned)
        {
            return;
        }

        var valid = Create(Id, email, displayName, CreatedAt);
        Email = valid.Email;
        NormalizedEmail = valid.NormalizedEmail;
        DisplayName = valid.DisplayName;
    }

    /// <summary>Creates a local account.</summary>
    /// <param name="id">New id.</param>
    /// <param name="email">Email address.</param>
    /// <param name="displayName">Name.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The user.</returns>
    public static User Create(Guid id, string email, string displayName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(displayName);
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || at == trimmed.Length - 1 || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            throw new DomainValidationException("email", "email-invalid");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainValidationException("name", "name-required");
        }

        return new User(id, trimmed, displayName.Trim(), now);
    }
}
