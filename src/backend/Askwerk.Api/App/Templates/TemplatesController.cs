// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Askwerk.Infrastructure.Templates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Templates;

/// <summary>
/// Survey templates (AWK-17 step 5, W10 C): Askwerk's own, then the organisation's. Editors save a survey's draft as a
/// template and delete their organisation's; Askwerk's stay.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/templates")]
public sealed class TemplatesController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Editors = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];

    /// <summary>Askwerk's templates, then the organisation's by age.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The templates.</returns>
    [HttpGet]
    [ProducesResponseType<TemplateListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TemplateListResponse>> List(CancellationToken cancellationToken)
    {
        var own = await context.SurveyTemplates.OrderBy(template => template.CreatedAt).ToListAsync(cancellationToken);
        var askwerk = SystemTemplates.All.Select(template => new TemplateResponse(template.Id, "askwerk", null, template.Names, Count(template.Definition), template.Definition.Languages));
        var organisation = own.Select(template =>
        {
            var definition = JsonSerializer.Deserialize<SurveyDefinition>(template.Definition, SurveyDefinitionJson.Options)!;

            return new TemplateResponse(template.Id.ToString(), "organisation", template.Name, null, Count(definition), definition.Languages);
        });

        return new TemplateListResponse([.. askwerk, .. organisation]);
    }

    /// <summary>Saves a survey's draft as the organisation's template.</summary>
    /// <param name="request">The survey and the template's name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the id; 400 at the field; 403 for viewers and analysts.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Save(SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (User.TenantId() is not { } tenantId || !await MayEditAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        var draft = await context.SurveyVersions.FirstOrDefaultAsync(version => version.SurveyId == request.SurveyId && version.State == SurveyVersionState.Draft, cancellationToken);

        if (draft is null)
        {
            ModelState.AddModelError("surveyId", "survey-not-found");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError("name", "name-required");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var template = SurveyTemplate.Create(Guid.CreateVersion7(now), tenantId, request.Name!, draft!.Definition, User.UserId()!.Value, now);
        context.SurveyTemplates.Add(template);
        await AuditAsync(template, "template.saved", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/templates/{template.Id}", new CreatedTemplateResponse(template.Id.ToString()));
    }

    /// <summary>Deletes one of the organisation's templates; surveys made from it keep their copy.</summary>
    /// <param name="id">The template.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for viewers and analysts; 404 when not the organisation's.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (User.TenantId() is not { } tenantId || !await MayEditAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        var template = Guid.TryParse(id, out var templateId) ? await context.SurveyTemplates.FirstOrDefaultAsync(row => row.Id == templateId, cancellationToken) : null;

        if (template is null)
        {
            return NotFound();
        }

        context.SurveyTemplates.Remove(template);
        await AuditAsync(template, "template.deleted", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>A template's definition: Askwerk's by "askwerk:…", the organisation's by its id; null when there is none.</summary>
    /// <param name="context">A context in the user's organisation.</param>
    /// <param name="id">The template id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The definition or null.</returns>
    internal static async Task<SurveyDefinition?> DefinitionOfAsync(AskwerkDbContext context, string id, CancellationToken cancellationToken)
    {
        if (SystemTemplates.Find(id) is { } system)
        {
            return system.Definition;
        }

        if (!Guid.TryParse(id, out var templateId))
        {
            return null;
        }

        var json = await context.SurveyTemplates.Where(template => template.Id == templateId).Select(template => template.Definition).FirstOrDefaultAsync(cancellationToken);

        return json is null ? null : JsonSerializer.Deserialize<SurveyDefinition>(json, SurveyDefinitionJson.Options);
    }

    private static int Count(SurveyDefinition definition) => definition.Pages.Sum(page => page.Questions.Count);

    private async Task<bool> MayEditAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Editors.Contains(known);
    }

    private Task AuditAsync(SurveyTemplate template, string action, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), template.TenantId, AuditActorKind.User, User.UserId().ToString()!, action, "SurveyTemplate", template.Id.ToString(), Diff: null, trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>The templates to choose from.</summary>
/// <param name="Items">Askwerk's first, then the organisation's.</param>
public sealed record TemplateListResponse(IReadOnlyList<TemplateResponse> Items);

/// <summary>One template.</summary>
/// <param name="Id">"askwerk:…" or the organisation's template id.</param>
/// <param name="Scope">askwerk or organisation.</param>
/// <param name="Name">The organisation's templates: its name.</param>
/// <param name="Names">Askwerk's templates: the name per language.</param>
/// <param name="Questions">How many questions it has.</param>
/// <param name="Languages">Its languages.</param>
public sealed record TemplateResponse(string Id, string Scope, string? Name, TranslatedText? Names, int Questions, IReadOnlyList<string> Languages);

/// <summary>A survey's draft to save as a template.</summary>
/// <param name="SurveyId">The survey.</param>
/// <param name="Name">The template's name.</param>
public sealed record SaveTemplateRequest(Guid SurveyId, string? Name);

/// <summary>A saved template.</summary>
/// <param name="Id">Its id.</param>
public sealed record CreatedTemplateResponse(string Id);
