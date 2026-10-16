// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Domain.Instances;

namespace Askwerk.Application.Setup;

/// <summary>Reads and adds tenants and memberships; changes are stored by the next unit of work.</summary>
public interface ITenantRepository
{
    /// <summary>Whether any tenant uses the slug, across the whole installation.</summary>
    /// <param name="slug">The slug.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when taken.</returns>
    Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Adds a tenant.</summary>
    /// <param name="tenant">The tenant.</param>
    void Add(Tenant tenant);

    /// <summary>Adds a membership.</summary>
    /// <param name="membership">The membership.</param>
    void Add(Membership membership);
}

/// <summary>Reads and adds the setup record of the installation.</summary>
public interface IInstanceSettingsRepository
{
    /// <summary>Returns the settings, or null while setup has not run.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings or null.</returns>
    Task<InstanceSettings?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Adds the settings.</summary>
    /// <param name="settings">The settings.</param>
    void Add(InstanceSettings settings);
}

/// <summary>Local accounts, backed by ASP.NET Core Identity (password rules, hashing). Never hand-rolled.</summary>
public interface IAccounts
{
    /// <summary>Checks the password against the policy, hashes it and adds the user (stored by the next unit of work).</summary>
    /// <param name="user">The new user.</param>
    /// <param name="password">The chosen password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Null on success, otherwise the code of the broken password rule, e.g. <c>password-too-short</c>.</returns>
    Task<string?> CreateAsync(User user, string password, CancellationToken cancellationToken);
}

/// <summary>Tells the database which tenant and user the following changes are for (row-level security).</summary>
public interface IDataScope
{
    /// <summary>Works in the given tenant from now on.</summary>
    /// <param name="tenantId">The tenant.</param>
    void UseTenant(Guid tenantId);

    /// <summary>Acts as the given user from now on.</summary>
    /// <param name="userId">The user.</param>
    void UseUser(Guid userId);
}

/// <summary>Encrypts secrets that must be stored (SMTP password) with the instance's keys.</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts a secret for storage.</summary>
    /// <param name="secret">Plain text.</param>
    /// <returns>Cipher text.</returns>
    string Protect(string secret);

    /// <summary>Decrypts a stored secret.</summary>
    /// <param name="protectedSecret">Cipher text.</param>
    /// <returns>Plain text.</returns>
    string Unprotect(string protectedSecret);
}
