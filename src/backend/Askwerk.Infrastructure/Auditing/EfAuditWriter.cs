// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Auditing;
using Askwerk.Domain.Auditing;
using Askwerk.Infrastructure.Persistence;

namespace Askwerk.Infrastructure.Auditing;

/// <summary>Queues audit content on the context; <see cref="EfUnitOfWork"/> chains and stores it in the same transaction.</summary>
public sealed class EfAuditWriter(AskwerkDbContext context) : IAuditWriter
{
    /// <inheritdoc />
    public Task WriteAsync(AuditContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        context.PendingAudit.Add(content);

        return Task.CompletedTask;
    }
}
