// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Setup;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Instances;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Infrastructure.Identity;

/// <summary>Tenants and memberships through EF Core, under row-level security.</summary>
public sealed class EfTenantRepository(AskwerkDbContext context) : ITenantRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Other tenants are invisible under row-level security, so this asks the database function
    /// <c>tenant_slug_taken</c>, which answers only yes or no and reveals nothing else.
    /// </remarks>
    public Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken) =>
        context.Database.SqlQuery<bool>($"SELECT tenant_slug_taken({slug}) AS \"Value\"").SingleAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Tenant tenant) => context.Tenants.Add(tenant);

    /// <inheritdoc />
    public void Add(Membership membership) => context.Memberships.Add(membership);
}

/// <summary>Users through EF Core; users are instance-wide, not tenant rows.</summary>
public sealed class EfUserRepository(AskwerkDbContext context)
{
    /// <summary>Adds a user; stored by the next unit of work.</summary>
    /// <param name="user">The user.</param>
    public void Add(User user) => context.Users.Add(user);
}

/// <summary>The instance settings written by setup.</summary>
public sealed class EfInstanceSettingsRepository(AskwerkDbContext context) : IInstanceSettingsRepository
{
    /// <inheritdoc />
    public Task<InstanceSettings?> FindAsync(CancellationToken cancellationToken) =>
        context.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(InstanceSettings settings) => context.InstanceSettings.Add(settings);
}

/// <summary>Points the session's row-level security values at a tenant and a user.</summary>
public sealed class DbDataScope(DbSessionContext session) : IDataScope
{
    /// <inheritdoc />
    public void UseTenant(Guid tenantId) => session.TenantId = tenantId;

    /// <inheritdoc />
    public void UseUser(Guid userId) => session.UserId = userId;
}
