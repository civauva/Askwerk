// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Askwerk.Api.Identity;

/// <summary>
/// Whether an organisation is open (AWK-38): suspended and deleted ones are closed to sessions, keys and survey links.
/// Read once per 30 seconds per organisation; a change by an installation administrator applies at once here.
/// </summary>
public sealed class TenantStates(IMemoryCache cache)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(30);

    /// <summary>Whether the organisation is active.</summary>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="context">A database whose session is in that organisation (row-level security).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when it works.</returns>
    public async Task<bool> IsOpenAsync(Guid tenantId, AskwerkDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (cache.TryGetValue(Key(tenantId), out bool open))
        {
            return open;
        }

        var status = await context.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => (TenantStatus?)tenant.Status).FirstOrDefaultAsync(cancellationToken);
        open = status == TenantStatus.Active;
        cache.Set(Key(tenantId), open, KeepFor);

        return open;
    }

    /// <summary>The organisation's status changed: the next request reads it again.</summary>
    /// <param name="tenantId">The organisation.</param>
    public void Changed(Guid tenantId) => cache.Remove(Key(tenantId));

    private static string Key(Guid tenantId) => $"tenant-open:{tenantId:N}";
}
