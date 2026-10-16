// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Identity;
using Askwerk.Application.Setup;
using Askwerk.Domain.Auditing;
using Askwerk.Infrastructure.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Auditing;

/// <summary>Checks the instance audit chain for tampering (App API; the GUI button comes later). Instance owner only.</summary>
[ApiController]
[Route("api/app/audit/verify")]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
public sealed class AuditVerificationController(AuditLogReader reader, IInstanceSettingsRepository settings) : ControllerBase
{
    /// <summary>Recomputes every hash of the instance audit chain.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Whether the chain is intact, how many entries it has, and where it breaks.</returns>
    [HttpGet]
    [ProducesResponseType<AuditVerificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AuditVerificationResponse>> Get(CancellationToken cancellationToken)
    {
        // The instance chain is the installation's record: only its owner checks it.
        if ((await settings.FindAsync(cancellationToken))?.OwnerUserId != User.UserId())
        {
            return Forbid();
        }

        var chain = await reader.ReadChainAsync(tenantId: null, cancellationToken);
        var result = AuditChain.Verify(chain);

        return new AuditVerificationResponse(result.IsIntact, chain.Count, result.BrokenAtSequence);
    }
}

/// <summary>Result of an audit chain check.</summary>
/// <param name="IsIntact">True when no entry was changed, removed or inserted.</param>
/// <param name="Entries">Number of entries checked.</param>
/// <param name="BrokenAtSequence">The first entry that does not match, or null.</param>
public sealed record AuditVerificationResponse(bool IsIntact, int Entries, long? BrokenAtSequence);
