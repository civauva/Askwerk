// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Auditing;
using Askwerk.Domain.Auditing;

namespace Askwerk.Tests.Application.Fakes;

internal sealed class RecordingAuditWriter : IAuditWriter
{
    public List<AuditContent> Pending { get; } = [];

    public List<AuditContent> Written { get; } = [];

    public Task WriteAsync(AuditContent content, CancellationToken cancellationToken)
    {
        Pending.Add(content);
        return Task.CompletedTask;
    }
}
