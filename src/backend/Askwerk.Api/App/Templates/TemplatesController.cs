// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.App.Surveys;
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
        // An edited template's definition and look live in its editing survey (W32).
        var own = await context.SurveyTemplates
            .OrderBy(template => template.CreatedAt)
            .Select(template => new
            {
                template,
                Survey = context.Surveys.Where(survey => survey.Id == template.SurveyId).Select(survey => new { survey.Look, survey.ThemeId }).FirstOrDefault(),
                Draft = context.SurveyVersions.Where(draft => draft.SurveyId == template.SurveyId && draft.State == SurveyVersionState.Draft).Select(draft => draft.Definition).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var fronts = await SurveyFronts.LoadAsync(context, cancellationToken);
        var askwerk = SystemTemplates.All.Select(template =>
            new TemplateResponse(template.Id, "askwerk", null, template.Names, Count(template.Definition), template.Definition.Languages, null, fronts.Of(template.Definition, null, null)));
        var organisation = own.Select(row =>
        {
            var definition = JsonSerializer.Deserialize<SurveyDefinition>(row.Draft ?? row.template.Definition, SurveyDefinitionJson.Options)!;

            return new TemplateResponse(row.template.Id.ToString(), "organisation", row.template.Name, null, Count(definition), definition.Languages, row.template.SurveyId, fronts.Of(definition, row.Survey?.Look, row.Survey?.ThemeId));
        });

        return new TemplateListResponse([.. askwerk, .. organisation]);
    }

    /// <summary>
    /// Opens one of the organisation's templates for editing (W32): the designer edits it in a survey of its own that is
    /// never listed, published or answered. Made on the first edit; later edits find the same one.
    /// </summary>
    /// <param name="id">The template.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The survey to open; 403 for viewers and analysts; 404 when not the organisation's.</returns>
    [HttpPost("{id}/edit")]
    [ProducesResponseType<TemplateEditResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
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

        if (template.SurveyId is { } existing && await context.Surveys.AnyAsync(survey => survey.Id == existing && survey.DeletedAt == null, cancellationToken))
        {
            return Ok(new TemplateEditResponse(template.Id.ToString(), existing));
        }

        var surveyId = EditingSurvey(tenantId, template.Name, JsonSerializer.Deserialize<SurveyDefinition>(template.Definition, SurveyDefinitionJson.Options)!);
        template.EditIn(surveyId);
        await AuditAsync(template, "template.edited", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new TemplateEditResponse(template.Id.ToString(), surveyId));
    }

    /// <summary>Copies one of Askwerk's templates to the organisation's (W32), ready to edit; Askwerk's stays as it is.</summary>
    /// <param name="id">"askwerk:…".</param>
    /// <param name="request">The copy's name; the template's own when empty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the copy and its editing survey; 403 for viewers and analysts; 404 for an unknown template.</returns>
    [HttpPost("{id}/copy")]
    [ProducesResponseType<TemplateEditResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Copy(string id, CopyTemplateRequest? request, CancellationToken cancellationToken)
    {
        if (User.TenantId() is not { } tenantId || !await MayEditAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        if (SystemTemplates.Find(id) is not { } system)
        {
            return NotFound();
        }

        var now = time.GetUtcNow();
        var name = !string.IsNullOrWhiteSpace(request?.Name) ? request.Name : system.Names.TryGetValue(request?.Language ?? "en", out var named) ? named : system.Names.Values.First();
        var template = SurveyTemplate.Create(Guid.CreateVersion7(now), tenantId, name, JsonSerializer.Serialize(system.Definition, SurveyDefinitionJson.Options), User.UserId()!.Value, now);
        var surveyId = EditingSurvey(tenantId, template.Name, system.Definition);
        template.EditIn(surveyId);
        context.SurveyTemplates.Add(template);
        await AuditAsync(template, "template.saved", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/templates/{template.Id}", new TemplateEditResponse(template.Id.ToString(), surveyId));
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

        // Its editing survey goes with it (W32).
        if (template.SurveyId is { } surveyId && await context.Surveys.FirstOrDefaultAsync(survey => survey.Id == surveyId, cancellationToken) is { } survey)
        {
            survey.Delete(time.GetUtcNow());
        }

        context.SurveyTemplates.Remove(template);
        await AuditAsync(template, "template.deleted", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// A template's definition and look: Askwerk's by "askwerk:…", the organisation's by its id, from its editing survey
    /// once edited (W32); a null definition when there is none.
    /// </summary>
    /// <param name="context">A context in the user's organisation.</param>
    /// <param name="id">The template id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The definition or null, and the look if the template has one.</returns>
    internal static async Task<(SurveyDefinition? Definition, TemplateLook? Look)> SourceOfAsync(AskwerkDbContext context, string id, CancellationToken cancellationToken)
    {
        if (SystemTemplates.Find(id) is { } system)
        {
            return (system.Definition, null);
        }

        if (!Guid.TryParse(id, out var templateId))
        {
            return (null, null);
        }

        var row = await context.SurveyTemplates
            .Where(template => template.Id == templateId)
            .Select(template => new
            {
                template.Definition,
                Draft = context.SurveyVersions.Where(draft => draft.SurveyId == template.SurveyId && draft.State == SurveyVersionState.Draft).Select(draft => draft.Definition).FirstOrDefault(),
                Look = context.Surveys.Where(survey => survey.Id == template.SurveyId).Select(survey => new TemplateLook(survey.Look, survey.LookBase, survey.ThemeId)).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? (null, null) : (JsonSerializer.Deserialize<SurveyDefinition>(row.Draft ?? row.Definition, SurveyDefinitionJson.Options), row.Look);
    }

    /// <summary>A survey a template is edited in (W32), with the template's definition as its draft.</summary>
    private Guid EditingSurvey(Guid tenantId, string name, SurveyDefinition definition)
    {
        var now = time.GetUtcNow();
        var (survey, draft) = Survey.Create(Guid.CreateVersion7(now), tenantId, name, definition.Languages, definition.DefaultLanguage, User.UserId()!.Value, now);
        draft.ReplaceDefinition(definition, now);
        survey.DraftSaved(definition, now);
        context.Surveys.Add(survey);
        context.SurveyVersions.Add(draft);

        return survey.Id;
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
/// <param name="SurveyId">The organisation's templates, once edited: the survey the designer edits it in (W32).</param>
/// <param name="Front">The front page a card draws (W32).</param>
public sealed record TemplateResponse(string Id, string Scope, string? Name, TranslatedText? Names, int Questions, IReadOnlyList<string> Languages, Guid? SurveyId = null, SurveyFront? Front = null);

/// <summary>A template to edit in the designer (W32).</summary>
/// <param name="Id">The template.</param>
/// <param name="SurveyId">The survey to open.</param>
public sealed record TemplateEditResponse(string Id, Guid SurveyId);

/// <summary>A copy of one of Askwerk's templates (W32).</summary>
/// <param name="Name">The copy's name; the template's own when empty.</param>
/// <param name="Language">The language of the template's own name to take.</param>
public sealed record CopyTemplateRequest(string? Name, string? Language);

/// <summary>The look a template brings to a new survey (W32).</summary>
/// <param name="Tokens">Its own look as tokens JSON, if any.</param>
/// <param name="LookBase">Where that look started.</param>
/// <param name="ThemeId">Its theme, if any.</param>
public sealed record TemplateLook(string? Tokens, string? LookBase, Guid? ThemeId);

/// <summary>A survey's draft to save as a template.</summary>
/// <param name="SurveyId">The survey.</param>
/// <param name="Name">The template's name.</param>
public sealed record SaveTemplateRequest(Guid SurveyId, string? Name);

/// <summary>A saved template.</summary>
/// <param name="Id">Its id.</param>
public sealed record CreatedTemplateResponse(string Id);
