// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Infrastructure.Auditing;

/// <summary>Reads audit chains for verification and display.</summary>
public sealed class AuditLogReader(AskwerkDbContext context)
{
    /// <summary>Reads a whole chain in sequence order.</summary>
    /// <param name="tenantId">The tenant, or null for the instance chain.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries, first to last.</returns>
    public async Task<IReadOnlyList<AuditEntry>> ReadChainAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var rows = await context.AuditLog
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .OrderBy(row => row.Sequence)
            .ToListAsync(cancellationToken);

        return rows.Select(row => row.ToEntry()).ToList();
    }
}
