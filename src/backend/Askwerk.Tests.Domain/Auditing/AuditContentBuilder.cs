// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Auditing;

namespace Askwerk.Tests.Domain.Auditing;

/// <summary>Builds audit content for tests, so each test states only what matters to it.</summary>
internal sealed class AuditContentBuilder
{
    private DateTimeOffset _occurredAt = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
    private string _action = "instance.initialized";
    private string _entityId = "instance";

    public static AuditContentBuilder Default() => new();

    public AuditContentBuilder WithAction(string action)
    {
        _action = action;
        return this;
    }

    public AuditContentBuilder WithEntityId(string entityId)
    {
        _entityId = entityId;
        return this;
    }

    public AuditContentBuilder At(DateTimeOffset occurredAt)
    {
        _occurredAt = occurredAt;
        return this;
    }

    public AuditContent Build() => new(
        OccurredAt: _occurredAt,
        TenantId: null,
        ActorKind: AuditActorKind.System,
        ActorId: null,
        Action: _action,
        EntityType: "Instance",
        EntityId: _entityId,
        Diff: null,
        TraceId: "4bf92f3577b34da6a3ce929d0e0e4736");
}
