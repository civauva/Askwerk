// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Configuration;
using Askwerk.Api.Identity;
using Askwerk.Api.Startup;
using Askwerk.Application.Storage;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Askwerk.Api.App.Installation;

/// <summary>
/// Removes the data of deleted organisations (AWK-39, W30): at once when an installation administrator asks, otherwise
/// once <see cref="Tenant.GracePeriod"/> is over. Every row with the organisation's tenant_id goes, in any table but the
/// audit log, and so do its files; the organisation's row stays, marked removed, so the audit log keeps its name.
/// Runs with the database owner's connection, across row-level security.
/// </summary>
public sealed partial class TenantRemoval(AskwerkSettings askwerk, IFileStorage storage, TenantStates states, TimeProvider time, ILogger<TenantRemoval> logger)
{
    private static readonly string[] Kept = ["audit_log", "tenant"];

    /// <summary>What removing an organisation's data takes away, for the confirmation.</summary>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>The counts; null when the organisation is not deleted or already removed.</returns>
    public async Task<RemovalCounts?> CountAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);

        if (!await owner.Tenants.AnyAsync(row => row.Id == tenantId && row.Status == TenantStatus.Deleted && row.PurgedAt == null, cancellationToken))
        {
            return null;
        }

        return new RemovalCounts(
            await owner.Surveys.CountAsync(row => row.TenantId == tenantId, cancellationToken),
            await owner.Responses.CountAsync(row => row.TenantId == tenantId, cancellationToken),
            await owner.Memberships.CountAsync(row => row.TenantId == tenantId, cancellationToken) + await owner.MemberInvitations.CountAsync(row => row.TenantId == tenantId, cancellationToken),
            await owner.Assets.CountAsync(row => row.TenantId == tenantId, cancellationToken) + await owner.ApiKeys.CountAsync(row => row.TenantId == tenantId, cancellationToken) + await owner.Domains.CountAsync(row => row.TenantId == tenantId, cancellationToken));
    }

    /// <summary>Removes a deleted organisation's data for good.</summary>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="actorUserId">The installation administrator; null for the scheduled removal.</param>
    /// <param name="traceId">The request's trace id, if any.</param>
    /// <param name="cancellationToken">Cancels the removal; nothing is removed then.</param>
    /// <returns>False when the organisation is not deleted or already removed.</returns>
    public async Task<bool> RemoveAsync(Guid tenantId, Guid? actorUserId, string? traceId, CancellationToken cancellationToken)
    {
        await using var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString);
        var tenant = await owner.Tenants.FirstOrDefaultAsync(row => row.Id == tenantId && row.Status == TenantStatus.Deleted && row.PurgedAt == null, cancellationToken);

        if (tenant is null)
        {
            return false;
        }

        var files = await owner.Assets.Where(row => row.TenantId == tenantId).Select(row => row.StorageKey).ToListAsync(cancellationToken);
        files.AddRange(await owner.DataExports.Where(row => row.TenantId == tenantId && row.FileKey != null).Select(row => row.FileKey!).ToListAsync(cancellationToken));
        var counts = await CountAsync(tenantId, cancellationToken);
        var tables = await owner.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'tenant_id' AND table_name IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public')")
            .ToListAsync(cancellationToken);
        // Table names come from the catalogue, not from a request; only plain names are used.
        var left = tables.Except(Kept).Where(table => table.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')).ToList();
        var slug = tenant.Slug;
        var now = time.GetUtcNow();

        await using (var transaction = await owner.Database.BeginTransactionAsync(cancellationToken))
        {
            // Tables refer to each other without cascades; a table whose rows are still referred to waits for the next pass.
            while (left.Count > 0)
            {
                var blocked = new List<string>();

                foreach (var table in left)
                {
                    await transaction.CreateSavepointAsync("removal", cancellationToken);

                    try
                    {
#pragma warning disable EF1003 // The table name is a plain catalogue name (checked above); the id is a parameter.
                        await owner.Database.ExecuteSqlRawAsync("DELETE FROM \"" + table + "\" WHERE tenant_id = {0}", [tenantId], cancellationToken);
#pragma warning restore EF1003
                        await transaction.ReleaseSavepointAsync("removal", cancellationToken);
                    }
                    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                    {
                        await transaction.RollbackToSavepointAsync("removal", cancellationToken);
                        blocked.Add(table);
                    }
                }

                if (blocked.Count == left.Count)
                {
                    throw new InvalidOperationException($"The data of organisation {tenantId} could not be removed; these tables still refer to each other: {string.Join(", ", blocked)}.");
                }

                left = blocked;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // Recorded in its own transaction with the audit chain; should it fail, the next round finds the organisation
        // still unremoved, deletes nothing more and records it then.
        tenant.MarkPurged(now);
        var actor = actorUserId is null ? AuditActorKind.System : AuditActorKind.User;
        await new EfAuditWriter(owner).WriteAsync(new AuditContent(now, tenant.Id, actor, actorUserId?.ToString(), "organisation.removed", "Tenant", tenant.Id.ToString(), JsonSerializer.Serialize(new { name = tenant.Name, slug, counts }), traceId), cancellationToken);
        await new EfUnitOfWork(owner).SaveChangesAsync(cancellationToken);

        states.Changed(tenantId);

        foreach (var file in files)
        {
            try
            {
                storage.Delete(file);
            }
            catch (IOException exception)
            {
                LogFileLeft(logger, file, exception);
            }
        }

        LogRemoved(logger, tenantId);

        return true;
    }

    /// <summary>Removes every organisation deleted longer than <see cref="Tenant.GracePeriod"/> ago.</summary>
    /// <param name="cancellationToken">Cancels the round.</param>
    /// <returns>How many were removed.</returns>
    public async Task<int> RemoveDueAsync(CancellationToken cancellationToken)
    {
        var due = time.GetUtcNow() - Tenant.GracePeriod;
        List<Guid> ids;

        await using (var owner = AskwerkDbContext.Create(askwerk.OwnerConnectionString))
        {
            ids = await owner.Tenants.Where(row => row.Status == TenantStatus.Deleted && row.PurgedAt == null && row.DeletedAt <= due).Select(row => row.Id).ToListAsync(cancellationToken);
        }

        var removed = 0;

        foreach (var id in ids)
        {
            removed += await RemoveAsync(id, null, null, cancellationToken) ? 1 : 0;
        }

        return removed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The data of organisation {TenantId} is removed.")]
    private static partial void LogRemoved(ILogger logger, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file {Key} of a removed organisation could not be deleted.")]
    private static partial void LogFileLeft(ILogger logger, string key, Exception exception);
}

/// <summary>Removes the data of organisations deleted 30 days ago; looks once an hour (AWK-39).</summary>
public sealed partial class TenantRemovalService(TenantRemoval removal, DatabaseState state, TimeProvider time, ILogger<TenantRemovalService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!state.IsReady)
            {
                continue;
            }

            try
            {
                await removal.RemoveDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing the data of deleted organisations failed; it is tried again in an hour.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>What removing an organisation's data takes away (AWK-39).</summary>
/// <param name="Surveys">Surveys.</param>
/// <param name="Responses">Responses.</param>
/// <param name="Members">Members and open invitations.</param>
/// <param name="Other">Files, API keys and domains.</param>
public sealed record RemovalCounts(int Surveys, int Responses, int Members, int Other);
