// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Auditing;

/// <summary>What happened, by whom, to which entity. The part of an audit entry that the hash protects.</summary>
/// <param name="OccurredAt">When the change was committed (UTC).</param>
/// <param name="TenantId">The tenant, or null for instance-level entries (their own chain).</param>
/// <param name="ActorKind">Who acted: a user, an API key, an OAuth client, a job or the system.</param>
/// <param name="ActorId">Id of the actor; null for the system.</param>
/// <param name="Action">Stable dotted name, e.g. <c>survey.published</c>.</param>
/// <param name="EntityType">Type of the changed entity, e.g. <c>Survey</c>.</param>
/// <param name="EntityId">Id of the changed entity.</param>
/// <param name="Diff">Before/after diff as JSON for definitions and settings; otherwise null.</param>
/// <param name="TraceId">W3C trace id of the request or job that made the change.</param>
public sealed record AuditContent(
    DateTimeOffset OccurredAt,
    Guid? TenantId,
    AuditActorKind ActorKind,
    string? ActorId,
    string Action,
    string EntityType,
    string EntityId,
    string? Diff,
    string? TraceId);
