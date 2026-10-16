// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Instances;
using Askwerk.Domain.Instances;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Infrastructure.Persistence;

/// <summary>Stores the installation record in the <c>instance</c> table.</summary>
public sealed class EfInstanceRepository(AskwerkDbContext context) : IInstanceRepository
{
    /// <inheritdoc />
    public Task<Instance?> FindAsync(CancellationToken cancellationToken) =>
        context.Instances.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Instance instance) => context.Instances.Add(instance);
}
