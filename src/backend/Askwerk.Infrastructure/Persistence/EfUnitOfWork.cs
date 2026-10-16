// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Persistence;
using Askwerk.Domain.Auditing;
using Askwerk.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Infrastructure.Persistence;

/// <summary>
/// Stores pending changes and their audit entries in one transaction. Each audit chain is locked with a
/// transaction-scoped advisory lock while its next entries are appended, so concurrent writers cannot fork it.
/// </summary>
public sealed class EfUnitOfWork(AskwerkDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (var chain in context.PendingAudit.GroupBy(content => content.TenantId))
        {
            await AppendChainAsync(chain.Key, chain.ToList(), cancellationToken);
        }

        context.PendingAudit.Clear();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AppendChainAsync(Guid? tenantId, IReadOnlyList<AuditContent> contents, CancellationToken cancellationToken)
    {
        var lockKey = "audit:" + (tenantId?.ToString() ?? "instance");
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);

        var lastRow = await context.AuditLog
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .OrderByDescending(row => row.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        var previous = lastRow?.ToEntry();

        foreach (var content in contents)
        {
            previous = AuditChain.Append(previous, content);
            context.AuditLog.Add(AuditLogRow.From(previous));
        }
    }
}
