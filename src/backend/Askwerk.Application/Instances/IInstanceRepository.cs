// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Instances;

namespace Askwerk.Application.Instances;

/// <summary>Reads and adds the installation record.</summary>
public interface IInstanceRepository
{
    /// <summary>Returns the installation record, or null before the first start has completed.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The instance or null.</returns>
    Task<Instance?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Adds the installation record; it is stored by the next <see cref="Persistence.IUnitOfWork.SaveChangesAsync"/>.</summary>
    /// <param name="instance">The new record.</param>
    void Add(Instance instance);
}
