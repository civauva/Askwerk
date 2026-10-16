// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.App.Results;
using Askwerk.Api.Integrations;
using Askwerk.Application.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Exports;

/// <summary>
/// Public API: results exports as jobs (AWK-25, W14 part 2). Start one, poll it until it is ready, download its file
/// within 7 days. The same exporters and codebook as the designer (docs/api/exports.md).
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Authorize(Policy = PublicApi.ResponsesRead)]
[Route("api/v1")]
[Produces("application/json")]
public sealed class PublicExportsController(ExportService exports, IUnitOfWork unitOfWork, Jobs.JobRunner jobs) : ControllerBase
{
    /// <summary>Starts an export of a survey's responses.</summary>
    /// <remarks>
    /// Scope responses:read. Answers 202 with the export to poll (Location). With an Idempotency-Key header, the same
    /// key within 24 hours gives the same export. At most three exports of an organisation run at the same time.
    /// </remarks>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">Format (excel, csv, spss, stata, json), layout and values for csv and excel, language, and filter.</param>
    /// <param name="idempotencyKey">Makes a retried call safe.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The export.</returns>
    /// <response code="202">The export, queued.</response>
    /// <response code="400">unknown-format.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks responses:read.</response>
    /// <response code="404">survey-not-found.</response>
    /// <response code="409">not-published: the survey has no published version.</response>
    /// <response code="429">too-many-exports or rate-limited.</response>
    [HttpPost("surveys/{surveyId:guid}/exports")]
    [Consumes("application/json")]
    [ProducesResponseType<PublicExport>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Start(Guid surveyId, PublicExportRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var filter = request.Filter ?? new PublicExportFilter();
        var (export, refusal) = await exports.StartAsync(
            surveyId,
            new ExportRequest(request.Format ?? "", request.Layout, request.Values, filter.Status, filter.From, filter.To, filter.Version, request.Language),
            new ExportActor(null, User.ApiKeyId(), User.OAuthClientId()),
            idempotencyKey,
            cancellationToken);

        if (refusal is not null)
        {
            return PublicApi.Problem(HttpContext, refusal.Status, refusal.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        jobs.Nudge();
        var view = (await exports.FindAsync(export!.Id, cancellationToken))!;

        return Accepted($"/api/v1/exports/{export.Id}", PublicExport.Of(view, HttpContext));
    }

    /// <summary>Reads an export: its status, progress and, once ready, its file.</summary>
    /// <remarks>Scope responses:read. Poll every few seconds until the status is ready, failed or expired.</remarks>
    /// <param name="exportId">The export.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The export.</returns>
    /// <response code="200">The export.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks responses:read.</response>
    /// <response code="404">export-not-found.</response>
    /// <response code="429">rate-limited.</response>
    [HttpGet("exports/{exportId:guid}")]
    [ProducesResponseType<PublicExport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Get(Guid exportId, CancellationToken cancellationToken) =>
        await exports.FindAsync(exportId, cancellationToken) is { } view
            ? Ok(PublicExport.Of(view, HttpContext))
            : PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "export-not-found");

    /// <summary>Downloads an export's file.</summary>
    /// <remarks>Scope responses:read. Every download is in the organisation's audit log with the key as actor.</remarks>
    /// <param name="exportId">The export.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file.</returns>
    /// <response code="200">The file, as an attachment.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks responses:read.</response>
    /// <response code="404">export-not-found.</response>
    /// <response code="409">export-not-ready.</response>
    /// <response code="410">export-expired: files are kept 7 days.</response>
    /// <response code="429">rate-limited.</response>
    [HttpGet("exports/{exportId:guid}/file")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Download(Guid exportId, CancellationToken cancellationToken)
    {
        var (file, export, refusal) = await exports.OpenAsync(exportId, new ExportActor(null, User.ApiKeyId(), User.OAuthClientId()), cancellationToken);

        if (refusal is not null)
        {
            return PublicApi.Problem(HttpContext, refusal.Status, refusal.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return File(file!, export!.ContentType!, export.FileName);
    }
}

/// <summary>Which responses an export holds; all when empty.</summary>
/// <param name="Status">complete or partial.</param>
/// <param name="From">Started at or after (UTC).</param>
/// <param name="To">Started before (UTC).</param>
/// <param name="Version">Only this published version.</param>
public sealed record PublicExportFilter(string? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int? Version = null);

/// <summary>An export to start.</summary>
/// <param name="Format">excel, csv, spss, stata or json.</param>
/// <param name="Layout">csv and excel: wide (one row per response, the default) or long (one row per answer).</param>
/// <param name="Values">csv and excel: labels (the default), codes or both.</param>
/// <param name="Language">The language of labels; the survey's default when empty.</param>
/// <param name="Filter">Which responses.</param>
public sealed record PublicExportRequest(string? Format, string? Layout = null, string? Values = null, string? Language = null, PublicExportFilter? Filter = null);

/// <summary>How far an export is.</summary>
/// <param name="Done">Responses written.</param>
/// <param name="Total">Responses to write.</param>
public sealed record PublicExportProgress(int Done, int Total);

/// <summary>An export's file.</summary>
/// <param name="Name">The file name.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Url">Where to download it.</param>
public sealed record PublicExportFile(string Name, string ContentType, long Size, string Url);

/// <summary>An export.</summary>
/// <param name="Id">The export's id.</param>
/// <param name="SurveyId">The survey.</param>
/// <param name="Status">queued, running, ready, failed or expired.</param>
/// <param name="Format">excel, csv, spss, stata or json.</param>
/// <param name="Progress">Responses written of all to write.</param>
/// <param name="File">The file, once ready.</param>
/// <param name="Error">Why it failed, as a problem; null otherwise.</param>
/// <param name="CreatedAt">Started (UTC).</param>
/// <param name="FinishedAt">Ready or failed (UTC).</param>
/// <param name="ExpiresAt">When the file is deleted (UTC).</param>
public sealed record PublicExport(Guid Id, Guid SurveyId, string Status, string Format, PublicExportProgress Progress, PublicExportFile? File, ProblemDetails? Error, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, DateTimeOffset? ExpiresAt)
{
    /// <summary>The export of a view, in the caller's language.</summary>
    /// <param name="view">The export with its job.</param>
    /// <param name="context">The request.</param>
    /// <returns>The export.</returns>
    public static PublicExport Of(ExportView view, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(view);
        var export = view.Export;

        return new PublicExport(
            export.Id,
            export.SurveyId,
            view.Status,
            export.Format,
            new PublicExportProgress(export.Done, export.Total),
            view.Status == "ready" && export.FileName is not null ? new PublicExportFile(export.FileName, export.ContentType ?? "application/octet-stream", export.Size ?? 0, $"/api/v1/exports/{export.Id}/file") : null,
            view.Status == "failed" ? PublicProblems.Of(context, null, view.Error ?? "export-failed") : null,
            export.CreatedAt,
            export.FinishedAt,
            export.ExpiresAt);
    }
}
