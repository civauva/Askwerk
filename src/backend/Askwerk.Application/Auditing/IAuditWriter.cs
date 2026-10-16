// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Auditing;

namespace Askwerk.Application.Auditing;

/// <summary>
/// The single write path for audit entries (agents/observability.md). An entry written here is stored
/// by the same <see cref="Persistence.IUnitOfWork.SaveChangesAsync"/> as the change it describes.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Appends an entry to the chain of its tenant, or of the instance when the tenant is null.</summary>
    /// <param name="content">The audited facts.</param>
    /// <param name="cancellationToken">Cancels reading the chain's last entry.</param>
    /// <returns>A task that completes when the entry is queued for the transaction.</returns>
    Task WriteAsync(AuditContent content, CancellationToken cancellationToken);
}
