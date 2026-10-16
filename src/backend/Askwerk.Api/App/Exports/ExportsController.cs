// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.App.Results;
using Askwerk.Api.Identity;
using Askwerk.Application.Persistence;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Exports;

/// <summary>
/// Exports in the designer (AWK-25, W14 A): ask for one, follow its progress, see the survey's recent exports and
/// download the file. Owners, admins, editors and analysts export; viewers read results but do not export.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app")]
public sealed class ExportsController(AskwerkDbContext context, ExportService exports, IUnitOfWork unitOfWork, Jobs.JobRunner jobs) : ControllerBase
{
    private static readonly TenantRole[] Exporters = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor, TenantRole.Analyst];

    /// <summary>Asks for an export of the results as filtered; a job prepares it.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">Format, layout, values, language and filter.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202 with the export; 400 unknown-format; 403 for viewers; 404; 409 not-published; 429 too-many-exports.</returns>
    [HttpPost("surveys/{surveyId:guid}/exports")]
    [ProducesResponseType<ExportItem>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Start(Guid surveyId, ExportRequest request, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var (export, refusal) = await exports.StartAsync(surveyId, request, new ExportActor(User.UserId(), null), null, cancellationToken);

        if (refusal is not null)
        {
            return Problem(refusal);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        jobs.Nudge();

        return StatusCode(StatusCodes.Status202Accepted, ExportItem.Of((await exports.FindAsync(export!.Id, cancellationToken))!));
    }

    /// <summary>The survey's exports of the last seven days, newest first (W14 A4).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The exports; 403 for viewers.</returns>
    [HttpGet("surveys/{surveyId:guid}/exports")]
    [ProducesResponseType<ExportList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Recent(Guid surveyId, CancellationToken cancellationToken) =>
        await RefusedAsync(cancellationToken) ?? Ok(new ExportList([.. (await exports.RecentAsync(surveyId, cancellationToken)).Select(ExportItem.Of)]));

    /// <summary>One export, with its progress and, once ready, its file.</summary>
    /// <param name="exportId">The export.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The export; 403 for viewers; 404 when it is not this organisation's.</returns>
    [HttpGet("exports/{exportId:guid}")]
    [ProducesResponseType<ExportItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid exportId, CancellationToken cancellationToken) =>
        await RefusedAsync(cancellationToken) ?? (await exports.FindAsync(exportId, cancellationToken) is { } view ? Ok(ExportItem.Of(view)) : NotFound());

    /// <summary>Downloads an export's file; every download is audited.</summary>
    /// <param name="exportId">The export.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file; 403 for viewers; 404; 409 export-not-ready; 410 export-expired.</returns>
    [HttpGet("exports/{exportId:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ResultsProblem>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Download(Guid exportId, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var (file, export, refusal) = await exports.OpenAsync(exportId, new ExportActor(User.UserId(), null), cancellationToken);

        if (refusal is not null)
        {
            return Problem(refusal);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return File(file!, export!.ContentType!, export.FileName);
    }

    private ObjectResult Problem(ExportRefusal refusal) =>
        StatusCode(refusal.Status, new ResultsProblem(refusal.Code, System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var tenantId = User.TenantId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Exporters.Contains(known) ? null : Problem(new ExportRefusal(StatusCodes.Status403Forbidden, "role-not-allowed"));
    }
}

/// <summary>An export's file.</summary>
/// <param name="Name">The file name.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record ExportFile(string Name, string ContentType, long Size);

/// <summary>An export as the designer shows it.</summary>
/// <param name="Id">The export.</param>
/// <param name="SurveyId">The survey.</param>
/// <param name="Status">queued, running, ready, failed or expired.</param>
/// <param name="Format">excel, csv, spss, stata or json.</param>
/// <param name="Done">Responses written.</param>
/// <param name="Total">Responses to write.</param>
/// <param name="File">The file, once ready.</param>
/// <param name="Error">Why it failed: export-failed or too-many-rows-for-excel.</param>
/// <param name="RequestedBy">The person or key who asked.</param>
/// <param name="ByApiKey">Whether a key asked.</param>
/// <param name="Attempt">Failed tries so far, while it will be tried again.</param>
/// <param name="RetryAt">The next try (UTC).</param>
/// <param name="CreatedAt">Asked for (UTC).</param>
/// <param name="FinishedAt">Ready or failed (UTC).</param>
/// <param name="ExpiresAt">When the file is deleted (UTC).</param>
public sealed record ExportItem(
    Guid Id,
    Guid SurveyId,
    string Status,
    string Format,
    int Done,
    int Total,
    ExportFile? File,
    string? Error,
    string RequestedBy,
    bool ByApiKey,
    int? Attempt,
    DateTimeOffset? RetryAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>The item of a view.</summary>
    /// <param name="view">The export with its job.</param>
    /// <returns>The item.</returns>
    public static ExportItem Of(ExportView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var export = view.Export;

        return new ExportItem(
            export.Id,
            export.SurveyId,
            view.Status,
            export.Format,
            export.Done,
            export.Total,
            view.Status == "ready" && export.FileName is not null ? new ExportFile(export.FileName, export.ContentType ?? "application/octet-stream", export.Size ?? 0) : null,
            view.Error,
            view.RequestedBy,
            view.ByApiKey,
            view.Attempt,
            view.RetryAt,
            export.CreatedAt,
            export.FinishedAt,
            export.ExpiresAt);
    }
}

/// <summary>A survey's recent exports.</summary>
/// <param name="Items">Newest first.</param>
public sealed record ExportList(IReadOnlyList<ExportItem> Items);
