// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Domains;

/// <summary>
/// Share → Embed → "Only on these sites" (AWK-13, W21 3): a survey may narrow the organisation's websites to some of
/// them; none uses the organisation's list.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys/{surveyId:guid}/embed-origins")]
public sealed class SurveyEmbeddingController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    /// <summary>The survey's websites and the organisation's to choose from.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Both lists; 404 when the survey does not exist.</returns>
    [HttpGet]
    [ProducesResponseType<SurveyEmbeddingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid surveyId, CancellationToken cancellationToken)
    {
        var survey = await context.Surveys.AsNoTracking().FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var organisation = await context.Tenants.AsNoTracking().Where(tenant => tenant.Id == survey.TenantId).Select(tenant => tenant.EmbedOrigins).SingleAsync(cancellationToken);

        return Ok(new SurveyEmbeddingResponse(survey.EmbedOrigins, organisation));
    }

    /// <summary>Narrows the survey's websites; owners, admins and editors.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">Some of the organisation's websites; none uses them all.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 origin-not-allowed; 403; 404.</returns>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Put(Guid surveyId, SurveyEmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var survey = await context.Surveys.FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var userId = User.UserId();
        var role = await context.Memberships.Where(row => row.UserId == userId && row.TenantId == survey.TenantId).Select(row => (TenantRole?)row.Role).FirstOrDefaultAsync(cancellationToken);

        if (role is not (TenantRole.Owner or TenantRole.Admin or TenantRole.Editor))
        {
            return Forbid();
        }

        var organisation = await context.Tenants.Where(tenant => tenant.Id == survey.TenantId).Select(tenant => tenant.EmbedOrigins).SingleAsync(cancellationToken);
        var chosen = (request.Origins ?? []).Select(EmbeddingController.Origin).ToList();

        if (chosen.Any(origin => origin is null || !organisation.Contains(origin)))
        {
            ModelState.AddModelError("origins", "origin-not-allowed");
            return ValidationProblem(ModelState);
        }

        survey.NarrowEmbedOrigins(chosen.Distinct()!, time.GetUtcNow());
        await audit.WriteAsync(new AuditContent(time.GetUtcNow(), survey.TenantId, AuditActorKind.User, userId.ToString()!, "survey.embedding-changed", "Survey", survey.Id.ToString(), JsonSerializer.Serialize(new { origins = survey.EmbedOrigins }), trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

/// <summary>A survey's websites.</summary>
/// <param name="Origins">The survey's own; empty uses the organisation's.</param>
/// <param name="Organisation">The organisation's websites to choose from.</param>
public sealed record SurveyEmbeddingResponse(IReadOnlyList<string> Origins, IReadOnlyList<string> Organisation);

/// <summary>A survey's websites to keep.</summary>
/// <param name="Origins">Some of the organisation's; none uses them all.</param>
public sealed record SurveyEmbeddingRequest(IReadOnlyList<string>? Origins);
