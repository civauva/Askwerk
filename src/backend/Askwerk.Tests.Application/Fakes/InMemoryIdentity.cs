// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Setup;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Instances;

namespace Askwerk.Tests.Application.Fakes;

/// <summary>Tenants, memberships, users and instance settings in memory; committed by <see cref="InMemoryStore"/>.</summary>
internal sealed class InMemoryStore : ITenantRepository, IInstanceSettingsRepository, IAccounts, IDataScope
{
    public List<object> Pending { get; } = [];

    public List<object> Saved { get; } = [];

    /// <summary>The tenant the scope pointed at when each object was added.</summary>
    public Dictionary<object, Guid?> TenantInScopeWhenAdded { get; } = [];

    public Guid? ScopeTenant { get; private set; }

    public Guid? ScopeUser { get; private set; }

    public HashSet<string> TakenSlugs { get; } = [];

    public IEnumerable<T> SavedOf<T>() => Saved.OfType<T>();

    public Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(TakenSlugs.Contains(slug) || Saved.OfType<Tenant>().Any(tenant => tenant.Slug == slug));

    public void Add(Tenant tenant) => Track(tenant);

    public void Add(Membership membership) => Track(membership);

    public Task<InstanceSettings?> FindAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Saved.OfType<InstanceSettings>().FirstOrDefault());

    public void Add(InstanceSettings settings) => Track(settings);

    /// <summary>Accepts passwords of 12 characters or more and stores a readable fake hash.</summary>
    public Task<string?> CreateAsync(User user, string password, CancellationToken cancellationToken)
    {
        if (password.Length < 12)
        {
            return Task.FromResult<string?>("password-too-short");
        }

        user.PasswordHash = "hashed:" + password;
        Track(user);

        return Task.FromResult<string?>(null);
    }

    public void UseTenant(Guid tenantId) => ScopeTenant = tenantId;

    public void UseUser(Guid userId) => ScopeUser = userId;

    public void Commit()
    {
        Saved.AddRange(Pending);
        Pending.Clear();
    }

    private void Track(object item)
    {
        Pending.Add(item);
        TenantInScopeWhenAdded[item] = ScopeTenant;
    }
}
