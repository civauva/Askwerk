// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Auditing;

namespace Askwerk.Infrastructure.Auditing;

/// <summary>Storage shape of an <see cref="AuditEntry"/> in the <c>audit_log</c> table.</summary>
public sealed class AuditLogRow
{
    /// <summary>Database key, in insertion order.</summary>
    public long Id { get; set; }

    /// <summary>Tenant, or null for the instance chain.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Position in the chain.</summary>
    public long Sequence { get; set; }

    /// <summary>When it happened (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Actor kind as its enum name.</summary>
    public string ActorKind { get; set; } = "";

    /// <summary>Actor id, if any.</summary>
    public string? ActorId { get; set; }

    /// <summary>Dotted action name.</summary>
    public string Action { get; set; } = "";

    /// <summary>Entity type.</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Entity id.</summary>
    public string EntityId { get; set; } = "";

    /// <summary>Before/after diff as JSON text.</summary>
    public string? Diff { get; set; }

    /// <summary>W3C trace id.</summary>
    public string? TraceId { get; set; }

    /// <summary>Hash of the previous entry.</summary>
    public string PreviousHash { get; set; } = "";

    /// <summary>Hash of this entry.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Creates the row for a chained entry.</summary>
    /// <param name="entry">The entry to store.</param>
    /// <returns>The row.</returns>
    public static AuditLogRow From(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AuditLogRow
        {
            TenantId = entry.Content.TenantId,
            Sequence = entry.Sequence,
            OccurredAt = entry.Content.OccurredAt,
            ActorKind = entry.Content.ActorKind.ToString(),
            ActorId = entry.Content.ActorId,
            Action = entry.Content.Action,
            EntityType = entry.Content.EntityType,
            EntityId = entry.Content.EntityId,
            Diff = entry.Content.Diff,
            TraceId = entry.Content.TraceId,
            PreviousHash = entry.PreviousHash,
            Hash = entry.Hash,
        };
    }

    /// <summary>Reads the row back as a domain entry, exactly as stored.</summary>
    /// <returns>The entry.</returns>
    public AuditEntry ToEntry()
    {
        var content = new AuditContent(
            OccurredAt: OccurredAt,
            TenantId: TenantId,
            ActorKind: Enum.Parse<AuditActorKind>(ActorKind),
            ActorId: ActorId,
            Action: Action,
            EntityType: EntityType,
            EntityId: EntityId,
            Diff: Diff,
            TraceId: TraceId);

        return new AuditEntry(Sequence, content, PreviousHash, Hash);
    }
}
