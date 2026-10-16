// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Instances;

namespace Askwerk.Application.Instances;

/// <summary>Creates the installation record on first start and audits it.</summary>
public sealed class InitializeInstanceHandler(
    IInstanceRepository instances,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace)
{
    /// <summary>Initialises the instance unless that already happened.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the instance exists.</returns>
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        var existing = await instances.FindAsync(cancellationToken);

        if (existing is not null)
        {
            return;
        }

        var now = time.GetUtcNow();
        var instance = new Instance(Guid.CreateVersion7(now), now);
        instances.Add(instance);

        var entry = new AuditContent(
            OccurredAt: now,
            TenantId: null,
            ActorKind: AuditActorKind.System,
            ActorId: null,
            Action: "instance.initialized",
            EntityType: "Instance",
            EntityId: instance.Id.ToString(),
            Diff: null,
            TraceId: trace.CurrentTraceId);
        await audit.WriteAsync(entry, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
