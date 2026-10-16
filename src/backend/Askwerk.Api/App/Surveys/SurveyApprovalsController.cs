// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>
/// Approvals of a survey's draft from Preview (AWK-28, W16): every member of the organisation may approve the saved
/// draft as it is, with an optional note; only the approver withdraws. Publishing never waits for an approval.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys/{id:guid}/approvals")]
public sealed class SurveyApprovalsController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    /// <summary>Every approval of the survey, newest first, and whether the current draft is approved.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The approvals; 404 when the survey is not found.</returns>
    [HttpGet]
    [ProducesResponseType<SurveyApprovalsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken)
    {
        var draft = await DraftAsync(id, cancellationToken);

        if (draft is null)
        {
            return NotFound();
        }

        var fingerprint = SurveyApproval.FingerprintOf(draft.Definition);
        var me = User.UserId();
        var rows = await context.SurveyApprovals
            .Where(approval => approval.SurveyId == id)
            .OrderByDescending(approval => approval.ApprovedAt)
            .Select(approval => new { approval, By = context.Users.Where(user => user.Id == approval.ApprovedBy).Select(user => user.DisplayName).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        var items = rows.Select(row => new SurveyApprovalItem(
            row.approval.Id,
            row.By ?? "",
            row.approval.ApprovedAt,
            row.approval.Note,
            row.approval.Fingerprint,
            row.approval.Version,
            row.approval.Fingerprint == fingerprint,
            row.approval.ApprovedBy == me)).ToList();
        var state = items.Any(item => item.Current) ? "approved" : items.Count > 0 ? "earlier" : "none";

        return Ok(new SurveyApprovalsResponse(fingerprint, state, items));
    }

    /// <summary>Approves the saved draft as Preview shows it.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">An optional note.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the approval's id; 400 at a note over 500 characters; 404 when the survey is not found.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedApprovalResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id, ApproveSurveyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = await DraftAsync(id, cancellationToken);

        if (draft is null)
        {
            return NotFound();
        }

        var approval = SurveyApproval.Approve(Guid.CreateVersion7(time.GetUtcNow()), draft, request.Note, User.UserId()!.Value, time.GetUtcNow());
        context.SurveyApprovals.Add(approval);
        await AuditAsync(approval, "survey.approved", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CreatedApprovalResponse(approval.Id));
    }

    /// <summary>Withdraws one's own approval.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="approvalId">The approval.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for someone else's approval; 404 when not found.</returns>
    [HttpDelete("{approvalId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Withdraw(Guid id, Guid approvalId, CancellationToken cancellationToken)
    {
        var approval = await context.SurveyApprovals.FirstOrDefaultAsync(row => row.Id == approvalId && row.SurveyId == id, cancellationToken);

        if (approval is null)
        {
            return NotFound();
        }

        if (approval.ApprovedBy != User.UserId())
        {
            return Forbid();
        }

        context.SurveyApprovals.Remove(approval);
        await AuditAsync(approval, "survey.approval-withdrawn", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private Task<SurveyVersion?> DraftAsync(Guid id, CancellationToken cancellationToken) =>
        context.SurveyVersions
            .Where(version => version.SurveyId == id && version.State == SurveyVersionState.Draft
                && context.Surveys.Any(survey => survey.Id == id && survey.DeletedAt == null))
            .FirstOrDefaultAsync(cancellationToken);

    private Task AuditAsync(SurveyApproval approval, string action, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(
                time.GetUtcNow(),
                approval.TenantId,
                AuditActorKind.User,
                User.UserId().ToString()!,
                action,
                "Survey",
                approval.SurveyId.ToString(),
                JsonSerializer.Serialize(new { draft = approval.Version, fingerprint = approval.Fingerprint, note = approval.Note }),
                trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>An approval.</summary>
/// <param name="Note">Optional, at most 500 characters.</param>
public sealed record ApproveSurveyRequest(string? Note);

/// <summary>A new approval.</summary>
/// <param name="Id">Its id, to withdraw it.</param>
public sealed record CreatedApprovalResponse(Guid Id);

/// <summary>The approvals of a survey (W16 C).</summary>
/// <param name="Fingerprint">The current draft's fingerprint.</param>
/// <param name="State">approved (this draft), earlier (an earlier draft only) or none.</param>
/// <param name="Items">Every approval, newest first.</param>
public sealed record SurveyApprovalsResponse(string Fingerprint, string State, IReadOnlyList<SurveyApprovalItem> Items);

/// <summary>One approval.</summary>
/// <param name="Id">Its id.</param>
/// <param name="By">The approver's name.</param>
/// <param name="At">When (UTC).</param>
/// <param name="Note">The approver's note.</param>
/// <param name="Fingerprint">The approved draft's fingerprint.</param>
/// <param name="Version">Number of the draft it was given for.</param>
/// <param name="Current">Whether it approves the current draft.</param>
/// <param name="Mine">Whether the signed-in user gave it, and may withdraw it.</param>
public sealed record SurveyApprovalItem(Guid Id, string By, DateTimeOffset At, string? Note, string Fingerprint, int Version, bool Current, bool Mine);
