// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;

namespace Askwerk.Api.Identity;

/// <summary>
/// Audits account events on the instance chain (users belong to the installation, not to a tenant):
/// sign-in success and failure, lockouts and two-factor changes (agents/observability.md).
/// </summary>
public sealed class AccountAudit(IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace)
{
    /// <summary>Writes one entry and stores it, together with any pending account changes.</summary>
    /// <param name="user">The user the event is about; also the actor.</param>
    /// <param name="action">Dotted action, e.g. <c>user.signed-in</c>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when stored.</returns>
    public async Task WriteAsync(User user, string action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var id = user.Id.ToString();
        await audit.WriteAsync(new AuditContent(time.GetUtcNow(), null, AuditActorKind.User, id, action, "User", id, Diff: null, trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
