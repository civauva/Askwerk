// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Jobs;

/// <summary>
/// Work that runs in the background (architecture.md: an own PostgreSQL job table, FOR UPDATE SKIP LOCKED). The first
/// kinds send invitations and reminders (AWK-7). A job that fails is tried again later, five times in all.
/// </summary>
public sealed class Job
{
    /// <summary>Tries before a job gives up.</summary>
    public const int MaxAttempts = 5;

    // For EF Core.
    private Job()
    {
        Kind = "";
        Payload = "{}";
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>What to do, e.g. send-invitations.</summary>
    public string Kind { get; private set; }

    /// <summary>JSON the kind reads, e.g. {"invitationId": "…"}.</summary>
    public string Payload { get; private set; }

    /// <summary>Not before (UTC).</summary>
    public DateTimeOffset RunAt { get; private set; }

    /// <summary>Tries so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>A worker has it until then (UTC).</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>Done (UTC).</summary>
    public DateTimeOffset? DoneAt { get; private set; }

    /// <summary>The last failure.</summary>
    public string? Error { get; private set; }

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new job.</summary>
    public static Job Create(Guid id, Guid tenantId, string kind, string payload, DateTimeOffset runAt, DateTimeOffset now) =>
        new() { Id = id, TenantId = tenantId, Kind = kind, Payload = payload, RunAt = runAt, CreatedAt = now };

    /// <summary>The job is done.</summary>
    public void Done(DateTimeOffset now)
    {
        DoneAt = now;
        LockedUntil = null;
        Error = null;
    }

    /// <summary>The job failed; it runs again later with growing pauses, or gives up after the last try.</summary>
    public void Failed(string error, DateTimeOffset now)
    {
        Error = error.Length > 2000 ? error[..2000] : error;
        LockedUntil = null;

        if (Attempts >= MaxAttempts)
        {
            DoneAt = now;
            return;
        }

        RunAt = now.AddMinutes(Math.Pow(4, Attempts));
    }
}
