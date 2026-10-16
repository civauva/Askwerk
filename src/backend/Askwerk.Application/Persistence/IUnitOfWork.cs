// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Application.Persistence;

/// <summary>Stores every pending change, including audit entries, in one database transaction.</summary>
public interface IUnitOfWork
{
    /// <summary>Commits all pending changes together, or none of them.</summary>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>A task that completes when the transaction has committed.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
