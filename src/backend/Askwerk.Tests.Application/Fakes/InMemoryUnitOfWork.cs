// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Persistence;

namespace Askwerk.Tests.Application.Fakes;

/// <summary>Commits what the fakes collected, all at once, like one database transaction.</summary>
internal sealed class InMemoryUnitOfWork(InMemoryInstanceRepository instances, RecordingAuditWriter audit) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        instances.Saved.AddRange(instances.Pending);
        instances.Pending.Clear();
        audit.Written.AddRange(audit.Pending);
        audit.Pending.Clear();
        return Task.CompletedTask;
    }
}
