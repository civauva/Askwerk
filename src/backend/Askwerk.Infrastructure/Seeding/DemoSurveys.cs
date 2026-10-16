// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Askwerk.Application.Storage;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Askwerk.Infrastructure.Samples;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Infrastructure.Seeding;

/// <summary>
/// Six richer demo surveys (AWK-36) beside the demo survey: two graphics-rich ones and four from different
/// industries, each published with its own look, its pictures and sample answers. Created once per organisation, with
/// ids derived from the organisation's, so a restart or a second instance never adds another.
/// </summary>
public static class DemoSurveys
{
    /// <summary>The demo's id in an organisation: always the same, so it is created only once.</summary>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="demoId">The demo, e.g. "museum".</param>
    /// <returns>A stable id.</returns>
    public static Guid IdFor(Guid tenantId, string demoId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"askwerk.demo:{demoId}:{tenantId:N}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Creates the demos every organisation does not have yet, as its first owner.</summary>
    /// <param name="ownerConnectionString">The database owner's connection: it sees every organisation.</param>
    /// <param name="storage">File storage, for the pictures.</param>
    /// <param name="time">The clock.</param>
    /// <param name="traceId">The current trace, for the audit log.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many demo surveys were created.</returns>
    public static async Task<int> EnsureAsync(string ownerConnectionString, IFileStorage storage, TimeProvider time, string? traceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(storage);
        await using var context = AskwerkDbContext.Create(ownerConnectionString);
        var owners = await context.Memberships
            .Where(membership => membership.Role == TenantRole.Owner)
            .GroupBy(membership => membership.TenantId)
            .Select(group => new { TenantId = group.Key, UserId = group.OrderBy(membership => membership.CreatedAt).Select(membership => membership.UserId).First() })
            .ToListAsync(cancellationToken);
        var wanted = owners.SelectMany(owner => SampleSurveys.Demos.Select(demo => (Id: IdFor(owner.TenantId, demo.Id), owner.TenantId, owner.UserId, Demo: demo))).ToList();
        var ids = wanted.Select(item => item.Id).ToList();
        var existing = await context.Surveys.Where(survey => ids.Contains(survey.Id)).Select(survey => survey.Id).ToListAsync(cancellationToken);
        var audit = new EfAuditWriter(context);
        // Stores the audit entries chained, in the same transaction as the survey.
        var unitOfWork = new EfUnitOfWork(context);
        var created = 0;

        foreach (var (id, tenantId, userId, demo) in wanted.Where(item => !existing.Contains(item.Id)))
        {
            var now = time.GetUtcNow();
            await SampleSurveyBuilder.CreateAsync(
                context,
                storage,
                demo,
                id,
                tenantId,
                userId,
                demo.Title?.GetValueOrDefault("en") ?? demo.Names.GetValueOrDefault("en") ?? demo.Id,
                close: false,
                (survey, action) => audit.WriteAsync(new AuditContent(now, survey.TenantId, AuditActorKind.System, "demo-survey", action, "Survey", survey.Id.ToString(), Diff: null, traceId), cancellationToken),
                () => unitOfWork.SaveChangesAsync(cancellationToken),
                now,
                cancellationToken);
            created++;
        }

        return created;
    }
}
