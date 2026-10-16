// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.App.Run;
using Askwerk.Api.App.Templates;
using Askwerk.Api.App.Themes;
using Askwerk.Application.Storage;
using Askwerk.Infrastructure.Pdf;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>
/// Surveys of the organisation chosen in the session (AWK-15 step 3, W6): the list, new surveys, the draft the
/// designer edits, and publishing. Row-level security limits every query to that organisation.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys")]
public sealed class SurveysController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;
    private static readonly TenantRole[] Editors = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];

    /// <summary>The organisation's surveys a page at a time (W13 A), last changed first, with counts per status.</summary>
    /// <param name="status">draft, published, closed or archived; all when empty.</param>
    /// <param name="search">Part of the title.</param>
    /// <param name="sort">title, status, responses or changed; a leading minus sorts descending. -changed when empty.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="limit">Rows per page, at most 100; 50 when empty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="cards">Also each survey's front page and look, for the Cards view (W32).</param>
    /// <returns>One page of surveys and the number of all that match.</returns>
    [HttpGet]
    [ProducesResponseType<SurveyListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SurveyListResponse>> List(string? status, string? search, string? sort, int offset, int? limit, CancellationToken cancellationToken, bool cards = false)
    {
        if (User.TenantId() is null)
        {
            return OrganisationNotChosen();
        }

        // A template's editing survey (W32) is not one of the organisation's surveys.
        var surveys = context.Surveys.Where(survey => survey.DeletedAt == null && !context.SurveyTemplates.Any(template => template.SurveyId == survey.Id));
        var counts = await surveys.GroupBy(survey => survey.Status).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken);

        // Archived surveys leave the list (AWK-29); asking for them by status still finds them.
        surveys = Enum.TryParse<SurveyStatus>(status, ignoreCase: true, out var wanted)
            ? surveys.Where(survey => survey.Status == wanted)
            : surveys.Where(survey => survey.Status != SurveyStatus.Archived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            surveys = surveys.Where(survey => EF.Functions.ILike(survey.Title, pattern));
        }

        var total = await surveys.CountAsync(cancellationToken);
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await Sorted(surveys, sort)
            .Skip(Math.Max(0, offset))
            .Take(size)
            .Select(survey => new
            {
                survey,
                CreatedBy = context.Users.Where(user => user.Id == survey.CreatedBy).Select(user => user.DisplayName).FirstOrDefault(),
                Responses = context.Responses.Count(response => response.SurveyId == survey.Id),
                HasChanges = survey.PublishedVersion != null && context.SurveyVersions.Any(draft =>
                    draft.SurveyId == survey.Id && draft.State == SurveyVersionState.Draft
                    && context.SurveyVersions.Any(published => published.SurveyId == survey.Id && published.Number == survey.PublishedVersion && published.Definition != draft.Definition)),
                Draft = cards ? context.SurveyVersions.Where(draft => draft.SurveyId == survey.Id && draft.State == SurveyVersionState.Draft).Select(draft => draft.Definition).FirstOrDefault() : null,
            })
            .ToListAsync(cancellationToken);
        var fronts = cards ? await SurveyFronts.LoadAsync(context, cancellationToken) : null;

        int Count(SurveyStatus key) => counts.Where(count => count.Key == key).Sum(count => count.Count);

        return new SurveyListResponse(
            [.. rows.Select(row => new SurveyListItem(
                row.survey.Id,
                row.survey.Title,
                Lower(row.survey.StatusAt(time.GetUtcNow())),
                row.survey.PublishedVersion,
                row.HasChanges,
                row.survey.Languages,
                row.CreatedBy ?? "",
                row.survey.UpdatedAt,
                row.Responses,
                fronts?.Of(row.Draft is null ? null : JsonSerializer.Deserialize<SurveyDefinition>(row.Draft, SurveyDefinitionJson.Options), row.survey.Look, row.survey.ThemeId)))],
            new Dictionary<string, int>
            {
                ["all"] = counts.Where(count => count.Key != SurveyStatus.Archived).Sum(count => count.Count),
                ["draft"] = Count(SurveyStatus.Draft),
                ["published"] = Count(SurveyStatus.Published),
                ["closed"] = Count(SurveyStatus.Closed),
            },
            total);
    }

    /// <summary>The list's order; ties go by id, so pages never overlap.</summary>
    private IQueryable<Survey> Sorted(IQueryable<Survey> surveys, string? sort)
    {
        var descending = sort?.StartsWith('-') ?? true;

        IOrderedQueryable<Survey> By<TKey>(System.Linq.Expressions.Expression<Func<Survey, TKey>> key) =>
            descending ? surveys.OrderByDescending(key) : surveys.OrderBy(key);

        IOrderedQueryable<Survey> ordered = sort?.TrimStart('-') switch
        {
            "title" => By(survey => survey.Title),
            "status" => By(survey => survey.Status),
            "responses" => By(survey => context.Responses.Count(response => response.SurveyId == survey.Id)),
            _ => By(survey => survey.UpdatedAt),
        };

        return descending ? ordered.ThenByDescending(survey => survey.Id) : ordered.ThenBy(survey => survey.Id);
    }

    /// <summary>Creates a survey with one empty page in the current organisation (editors, admins, owners).</summary>
    /// <param name="request">Title and languages.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the id; 400 at the field; 403 for other roles.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedSurveyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateSurveyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        // From a template: its definition is copied, and its languages win (AWK-17, W10 C).
        SurveyDefinition? template = null;
        TemplateLook? templateLook = null;

        if (request.TemplateId is { Length: > 0 } templateId)
        {
            (template, templateLook) = await TemplatesController.SourceOfAsync(context, templateId, cancellationToken);

            if (template is null)
            {
                ModelState.AddModelError("templateId", "template-not-found");
                return ValidationProblem(ModelState);
            }
        }

        var now = time.GetUtcNow();
        var (survey, draft) = Survey.Create(
            Guid.CreateVersion7(now),
            User.TenantId()!.Value,
            request.Title ?? "",
            template?.Languages ?? request.Languages ?? [],
            template?.DefaultLanguage ?? request.DefaultLanguage ?? "",
            User.UserId()!.Value,
            now);

        if (template is not null)
        {
            draft.ReplaceDefinition(template, now);
            survey.DraftSaved(template, now);

            // A template edited in the designer brings its look (W32).
            if (templateLook?.Tokens is { } tokens)
            {
                survey.UseLook(tokens, templateLook.LookBase, now);
            }
            else if (templateLook?.ThemeId is { } themeId)
            {
                survey.UseTheme(themeId, now);
            }
        }

        context.Surveys.Add(survey);
        context.SurveyVersions.Add(draft);
        await AuditAsync(survey, "survey.created", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/surveys/{survey.Id}/draft", new CreatedSurveyResponse(survey.Id));
    }

    /// <summary>What the survey's bar and tabs show (AWK-29, W17): title, status and counts. Every member reads it.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="settings">The instance's time zone, for the closing date.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The summary; 404 when not found.</returns>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType<SurveySummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summary(Guid id, [FromServices] IInstanceSettingsRepository settings, CancellationToken cancellationToken)
    {
        var row = await context.Surveys
            .Where(survey => survey.Id == id && survey.DeletedAt == null)
            .Select(survey => new
            {
                survey,
                Responses = context.Responses.Count(response => response.SurveyId == survey.Id),
                Participants = context.Participants.Count(participant => participant.SurveyId == survey.Id),
                TemplateId = context.SurveyTemplates.Where(template => template.SurveyId == survey.Id).Select(template => (Guid?)template.Id).FirstOrDefault(),
                HasChanges = survey.PublishedVersion != null && context.SurveyVersions.Any(draft =>
                    draft.SurveyId == survey.Id && draft.State == SurveyVersionState.Draft
                    && context.SurveyVersions.Any(published => published.SurveyId == survey.Id && published.Number == survey.PublishedVersion && published.Definition != draft.Definition)),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return NotFound();
        }

        var survey = row.survey;

        return Ok(new SurveySummaryResponse(
            survey.Id,
            survey.Title,
            Lower(survey.StatusAt(time.GetUtcNow())),
            survey.PublishedVersion,
            row.HasChanges,
            survey.Languages,
            row.Responses,
            row.Participants,
            survey.ClosesAt,
            (await settings.FindAsync(cancellationToken))?.TimeZone ?? "UTC",
            survey.OneResponsePerPerson,
            row.TemplateId?.ToString()));
    }

    /// <summary>Gives the survey a new internal title (W17 D).</summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">The title.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at the title; 403 for analysts and viewers; 404.</returns>
    [HttpPut("{id:guid}/title")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rename(Guid id, RenameSurveyRequest request, CancellationToken cancellationToken)
    {
        // A template's editing survey (W32) renames its template too.
        if (!string.IsNullOrWhiteSpace(request?.Title) && await context.SurveyTemplates.FirstOrDefaultAsync(template => template.SurveyId == id, cancellationToken) is { } template)
        {
            template.Rename(request.Title);
        }

        return await ChangeAsync(id, "survey.renamed", survey => survey.Rename(request?.Title ?? "", time.GetUtcNow()), cancellationToken);
    }

    /// <summary>Copies the draft into a new survey, a draft with no responses (W17 D).</summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">The copy's title.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the copy's id; 400 at the title; 403; 404.</returns>
    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType<CreatedSurveyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(Guid id, RenameSurveyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await FindAsync(id, cancellationToken) is not { } original)
        {
            return NotFound();
        }

        var definition = (await DraftOfAsync(id, cancellationToken)).ReadDefinition();
        var now = time.GetUtcNow();
        var (copy, draft) = Survey.Create(Guid.CreateVersion7(now), original.TenantId, request.Title ?? "", definition.Languages, definition.DefaultLanguage, User.UserId()!.Value, now);
        draft.ReplaceDefinition(definition, now);
        copy.DraftSaved(definition, now);
        copy.UseTheme(original.ThemeId, now);

        if (original.Look is { } look)
        {
            copy.UseLook(look, original.LookBase, now);
        }
        context.Surveys.Add(copy);
        context.SurveyVersions.Add(draft);
        await AuditAsync(copy, "survey.duplicated", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/surveys/{copy.Id}/draft", new CreatedSurveyResponse(copy.Id));
    }

    /// <summary>Stops taking responses (W17 D); the results stay.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404; 409 not-published.</returns>
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<PublishProblem>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Close(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "survey.closed", survey => survey.Close(time.GetUtcNow()), cancellationToken, survey => survey.StatusAt(time.GetUtcNow()) == SurveyStatus.Published ? null : "not-published");

    /// <summary>Takes responses again after closing.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404; 409 not-closed.</returns>
    [HttpPost("{id:guid}/reopen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<PublishProblem>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "survey.reopened", survey => survey.Reopen(time.GetUtcNow()), cancellationToken, survey => survey.StatusAt(time.GetUtcNow()) == SurveyStatus.Closed ? null : "not-closed");

    /// <summary>
    /// Sets or removes the closing date (W24). It is entered as a local date and time in the instance's time zone; from
    /// then on the runner refuses responses, and a job closes the survey.
    /// </summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">The date and time, e.g. 2026-10-31T18:00; null removes it.</param>
    /// <param name="settings">The instance's time zone.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at closesAt; 403; 404; 409 when closed or archived.</returns>
    [HttpPut("{id:guid}/closing-date")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<PublishProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetClosingDate(Guid id, SetClosingDateRequest request, [FromServices] IInstanceSettingsRepository settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var zone = (await settings.FindAsync(cancellationToken))?.TimeZone ?? "UTC";
        DateTimeOffset? closesAt = null;

        if (request.ClosesAt is not null)
        {
            if (ClosingDates.ToUtc(request.ClosesAt, zone) is not { } utc)
            {
                ModelState.AddModelError("closesAt", "closing-date-invalid");

                return ValidationProblem(ModelState);
            }

            closesAt = utc;
        }

        var now = time.GetUtcNow();

        return await ChangeAsync(
            id,
            closesAt is null ? "survey.closing-date-removed" : "survey.closing-date-set",
            survey =>
            {
                var before = survey.ClosesAt;
                survey.SetClosingDate(closesAt, now);

                if (closesAt is { } due)
                {
                    JobRunner.Enqueue(context, survey.TenantId, CloseSurveyJob.JobKind, new { surveyId = survey.Id }, due, now);
                }

                return JsonSerializer.Serialize(new { before, after = closesAt, timeZone = zone });
            },
            cancellationToken,
            survey => survey.StatusAt(now) is SurveyStatus.Draft or SurveyStatus.Published ? null : "not-collecting");
    }

    /// <summary>Turns one response per person on or off (W24): whether a browser that finished may answer again.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">On or off.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpPut("{id:guid}/one-response")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> SetOneResponse(Guid id, SetOneResponseRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            "survey.one-response-changed",
            survey =>
            {
                survey.AllowOneResponsePerPerson(request?.On ?? true, time.GetUtcNow());

                return JsonSerializer.Serialize(new { on = survey.OneResponsePerPerson });
            },
            cancellationToken);

    /// <summary>Puts the survey away: out of the list, no responses; its results stay (W17 D).</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "survey.archived", survey => survey.Archive(time.GetUtcNow()), cancellationToken);

    /// <summary>The draft the designer edits, with the last published definition for the rename checks.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The draft; 404 when the survey is not in this organisation.</returns>
    [HttpGet("{id:guid}/draft")]
    [ProducesResponseType<SurveyDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SurveyDraftResponse>> Draft(Guid id, CancellationToken cancellationToken)
    {
        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var draft = await DraftOfAsync(id, cancellationToken);
        var published = await PublishedOfAsync(survey, cancellationToken);

        return new SurveyDraftResponse(
            survey.Id,
            survey.Title,
            draft.Number,
            JsonDocument.Parse(draft.Definition).RootElement,
            published is null ? null : JsonDocument.Parse(published.Definition).RootElement,
            survey.ThemeId);
    }

    /// <summary>Saves the draft (autosave). Checked to be a definition; the publish checks run at publishing.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">The definition.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 when it is not a definition; 403 for viewers and analysts; 404 when not found.</returns>
    [HttpPut("{id:guid}/draft")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveDraft(Guid id, SaveDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var definition = ReadDefinition(request.Definition);

        if (definition is null)
        {
            ModelState.AddModelError("definition", "definition-invalid");
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        (await DraftOfAsync(id, cancellationToken)).ReplaceDefinition(definition, now);
        survey.DraftSaved(definition, now);
        await context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// The last saved draft in the runner's shape, for the preview (AWK-22, W9). Only editors of the organisation see
    /// drafts; nothing is started or stored.
    /// </summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The draft as the runner opens it, for every member of the organisation (W15 rev. 2); 404 when not found.</returns>
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType<RunSurveyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunSurveyResponse>> Preview(Guid id, CancellationToken cancellationToken)
    {
        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var draft = await DraftOfAsync(id, cancellationToken);
        var organisation = await context.Tenants.Where(tenant => tenant.Id == survey.TenantId).Select(tenant => tenant.Name).SingleAsync(cancellationToken);

        var theme = SurveyThemes.ToJson(await SurveyThemes.EffectiveAsync(context, survey, cancellationToken));

        return new RunSurveyResponse(new RunSurvey(survey.Id, organisation, "open", JsonDocument.Parse(draft.Definition).RootElement, theme), null);
    }

    /// <summary>Checks one page of the draft like the real survey does, without storing anything (preview, W9).</summary>
    /// <param name="id">The survey.</param>
    /// <param name="pageId">The page's id in the draft.</param>
    /// <param name="request">Answers by question id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 with the problems per question; 404 when not found.</returns>
    [HttpPut("{id:guid}/preview/pages/{pageId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckPreviewPage(Guid id, string pageId, SavePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await FindAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var page = (await DraftOfAsync(id, cancellationToken)).ReadDefinition().Pages.FirstOrDefault(page => page.Id == pageId);

        if (page is null)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        foreach (var (questionId, problem) in AnswerRules.CheckPage(page, request.Answers ?? new Dictionary<string, AnswerValue>(), today))
        {
            ModelState.AddModelError(questionId, problem);
        }

        return ModelState.IsValid ? NoContent() : ValidationProblem(ModelState);
    }

    /// <summary>The survey's links on the system address and every verified domain, the primary first (AWK-7, W12 B).</summary>
    /// <param name="id">The survey.</param>
    /// <param name="settings">Instance settings, for the system address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The links; 404 when the survey is not in this organisation.</returns>
    [HttpGet("{id:guid}/links")]
    [ProducesResponseType<SurveyLinksResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SurveyLinksResponse>> Links(Guid id, [FromServices] IInstanceSettingsRepository settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var survey = await FindAsync(id, cancellationToken);

        return survey is null ? NotFound() : new SurveyLinksResponse(survey.Code, await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken));
    }

    /// <summary>The survey's current draft as a printable A4 PDF in its theme (AWK-17 step 7, W10 E).</summary>
    /// <param name="id">The survey.</param>
    /// <param name="language">The language to print; the survey's default when it does not offer it.</param>
    /// <param name="host">The address the PDF's link and QR code show (AWK-7); the primary one when the survey has no link there.</param>
    /// <param name="storage">File storage, for images.</param>
    /// <param name="images">Makes images small enough for print and PNG where needed.</param>
    /// <param name="settings">Instance settings, for the survey's address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The PDF; 404 when the survey is not in this organisation.</returns>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(
        Guid id,
        [FromQuery] string? language,
        [FromQuery] string? host,
        [FromServices] IFileStorage storage,
        [FromServices] IImageProcessor images,
        [FromServices] IInstanceSettingsRepository settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(settings);
        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var definition = (await DraftOfAsync(id, cancellationToken)).ReadDefinition();
        var chosen = definition.Languages.Contains(language ?? "") ? language! : definition.DefaultLanguage;
        var theme = await SurveyThemes.EffectiveAsync(context, survey, cancellationToken);
        var organisation = await context.Tenants.Where(tenant => tenant.Id == survey.TenantId).Select(tenant => tenant.Name).SingleAsync(cancellationToken);
        var baseAddress = (await settings.FindAsync(cancellationToken))?.BaseAddress.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var wanted = new[] { definition.HeaderImage?.Asset, theme.HeaderImage?.Asset, theme.Logo?.Asset }
            .Concat(definition.Pages.SelectMany(page => page.Questions).Select(question => question.Image?.Asset))
            .Concat(definition.Pages.SelectMany(page => page.Questions).SelectMany(question => question.Options ?? []).Select(option => option.Image?.Asset))
            .OfType<string>()
            .Select(asset => Guid.TryParse(asset, out var assetId) ? assetId : Guid.Empty)
            .Where(assetId => assetId != Guid.Empty)
            .Distinct()
            .ToList();
        var pictures = new Dictionary<string, byte[]>();

        foreach (var asset in await context.Assets.Where(row => wanted.Contains(row.Id)).ToListAsync(cancellationToken))
        {
            await using var stream = storage.OpenRead(asset.StorageKey);

            if (stream is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            // At most 1600 px wide, and PNG for WebP, which the PDF library cannot place.
            var width = Math.Min(asset.Width ?? 1600, 1600);
            pictures[asset.Id.ToString()] = asset.ContentType == "image/jpeg" && width == asset.Width
                ? buffer.ToArray()
                : images.Resize(buffer.ToArray(), width, asset.ContentType == "image/jpeg" ? "image/jpeg" : "image/png");
        }

        var title = definition.Welcome?.Title is { } welcomeTitle && welcomeTitle.TryGetValue(chosen, out var translated) && !string.IsNullOrWhiteSpace(translated) ? translated : survey.Title;
        var pdf = SurveyPdf.Render(definition, theme, new SurveyPdfContent(title, organisation, await SurveyLinks.PrimaryAsync(context, settings, Request, survey, cancellationToken, host), chosen, pictures));

        return File(pdf, "application/pdf", $"{survey.Title}.pdf");
    }

    /// <summary>The survey's look (W26): the tokens it is shown with, whether they are its own, and where they started.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The look; 404 when not found.</returns>
    [HttpGet("{id:guid}/look")]
    [ProducesResponseType<SurveyLookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SurveyLookResponse>> Look(Guid id, CancellationToken cancellationToken)
    {
        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var tokens = SurveyThemes.ToJson(await SurveyThemes.EffectiveAsync(context, survey, cancellationToken));

        return new SurveyLookResponse(tokens, survey.Look is not null, survey.LookBase, survey.ThemeId);
    }

    /// <summary>
    /// Gives the survey its own look (W26): a look from the gallery, changed or not. Checked like a theme; errors refuse
    /// it. Not part of a version: published surveys follow at once.
    /// </summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">The tokens and the look they started from.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 with the check's findings; 403 for viewers and analysts; 404 when not found.</returns>
    [HttpPut("{id:guid}/look")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ThemeProblem>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetLook(Guid id, SetSurveyLookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        if (ThemeTokensJson.Read(request.Tokens) is not { } tokens)
        {
            return BadRequest(new ThemeProblem("theme-checks-failed", [new ThemeIssueResponse("tokens-invalid", "error", "tokens", null, null)], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var issues = ThemeChecks.Check(tokens);

        if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
        {
            return BadRequest(new ThemeProblem(
                "theme-checks-failed",
                [.. issues.Select(issue => new ThemeIssueResponse(issue.Code, issue.Severity.ToString().ToLowerInvariant(), issue.Path, issue.Against, issue.Ratio))],
                System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        survey.UseLook(JsonSerializer.Serialize(tokens, SurveyDefinitionJson.Options), request.LookBase, time.GetUtcNow());
        await AuditAsync(survey, "survey.look-changed", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Drops the survey's own look (W26): it shows with its theme, or the organisation's default, again.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for viewers and analysts; 404 when not found.</returns>
    [HttpDelete("{id:guid}/look")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DropLook(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        survey.UseTheme(survey.ThemeId, time.GetUtcNow());
        await AuditAsync(survey, "survey.look-changed", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Chooses the survey's theme, or the organisation's default with null (AWK-17, W10 B). Not part of a version.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="request">The theme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 for an unknown theme; 403 for viewers and analysts; 404 when not found.</returns>
    [HttpPut("{id:guid}/theme")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTheme(Guid id, SetSurveyThemeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        if (request.ThemeId is { } themeId && !await context.Themes.AnyAsync(theme => theme.Id == themeId, cancellationToken))
        {
            ModelState.AddModelError("themeId", "theme-not-found");
            return ValidationProblem(ModelState);
        }

        survey.UseTheme(request.ThemeId, time.GetUtcNow());
        await AuditAsync(survey, "survey.theme-chosen", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Publishes the draft after the publish checks (M3); renames become variable mappings.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="settings">Instance settings, for the link.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The version number and links; 409 with the findings that block it.</returns>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<PublishedSurveyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<PublishProblem>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(Guid id, [FromServices] IInstanceSettingsRepository settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var survey = await FindAsync(id, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        // A template is used, never published (W32).
        if (await context.SurveyTemplates.AnyAsync(template => template.SurveyId == id, cancellationToken))
        {
            return Conflict(new PublishProblem("template-not-publishable", [], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var draft = await DraftOfAsync(id, cancellationToken);
        var definition = draft.ReadDefinition();
        var previous = (await PublishedOfAsync(survey, cancellationToken))?.ReadDefinition();
        var issues = DefinitionChecks.Check(definition, previous);

        if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
        {
            return Conflict(new PublishProblem(
                "publish-checks-failed",
                [.. issues.Select(issue => new PublishIssue(issue.Code, Lower(issue.Severity), issue.Path, issue.Language))],
                System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var now = time.GetUtcNow();
        var tenantId = survey.TenantId;

        if (previous is not null)
        {
            context.VariableMappings.AddRange(DefinitionChecks.VariableRenames(previous, definition)
                .Select(rename => new VariableMapping(Guid.CreateVersion7(now), tenantId, survey.Id, rename.From, rename.To, draft.Number)));
        }

        var next = survey.Publish(draft, Guid.CreateVersion7(now), User.UserId()!.Value, now);
        var index = SurveyIndex.Build(draft, () => Guid.CreateVersion7(now));
        context.SurveyPages.AddRange(index.Pages);
        context.SurveyQuestions.AddRange(index.Questions);
        context.SurveyOptions.AddRange(index.Options);

        // The draft must be published (and stored) before the next draft exists: one draft per survey.
        // The published version keeps its draft's approvals; the entry says whether there was one (AWK-28, W16 E).
        var fingerprint = SurveyApproval.FingerprintOf(draft.Definition);
        var approved = await context.SurveyApprovals.AnyAsync(approval => approval.SurveyId == survey.Id && approval.Fingerprint == fingerprint, cancellationToken);
        await AuditAsync(survey, "survey.published", cancellationToken, JsonSerializer.Serialize(new { version = draft.Number, fingerprint, approved }));
        await Askwerk.Api.Integrations.WebhookService.RaiseAsync(context, survey.TenantId, "survey.published", new { surveyId = survey.Id, version = draft.Number }, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        context.SurveyVersions.Add(next);
        await context.SaveChangesAsync(cancellationToken);

        return Ok(new PublishedSurveyResponse(draft.Number, await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken)));
    }

    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private static SurveyDefinition? ReadDefinition(JsonElement json)
    {
        try
        {
            var definition = json.Deserialize<SurveyDefinition>(SurveyDefinitionJson.Options);

            return definition is { Languages.Count: > 0, Pages: not null } && definition.Languages.Contains(definition.DefaultLanguage) ? definition : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ObjectResult OrganisationNotChosen() =>
        StatusCode(StatusCodes.Status409Conflict, new PublishProblem("organisation-not-chosen", [], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    /// <summary>Null when the user may change surveys in the current organisation; otherwise the answer to give.</summary>
    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();

        if (tenantId is null)
        {
            return OrganisationNotChosen();
        }

        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Editors.Contains(known)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new PublishProblem("role-not-allowed", [], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
    }

    private Task<Survey?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Surveys.FirstOrDefaultAsync(survey => survey.Id == id && survey.DeletedAt == null, cancellationToken);

    private Task<SurveyVersion> DraftOfAsync(Guid id, CancellationToken cancellationToken) =>
        context.SurveyVersions.SingleAsync(version => version.SurveyId == id && version.State == SurveyVersionState.Draft, cancellationToken);

    private Task<SurveyVersion?> PublishedOfAsync(Survey survey, CancellationToken cancellationToken) =>
        survey.PublishedVersion is null
            ? Task.FromResult<SurveyVersion?>(null)
            : context.SurveyVersions.FirstOrDefaultAsync(version => version.SurveyId == survey.Id && version.Number == survey.PublishedVersion, cancellationToken);

    /// <summary>One change of the survey's menu: editors only, a refusal when the state does not allow it, audited.</summary>
    private Task<IActionResult> ChangeAsync(Guid id, string action, Action<Survey> change, CancellationToken cancellationToken, Func<Survey, string?>? refusedFor = null) =>
        ChangeAsync(
            id,
            action,
            survey =>
            {
                change(survey);

                return null;
            },
            cancellationToken,
            refusedFor);

    /// <summary>The same, with the audit entry's details from the change.</summary>
    private async Task<IActionResult> ChangeAsync(Guid id, string action, Func<Survey, string?> change, CancellationToken cancellationToken, Func<Survey, string?>? refusedFor = null)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await FindAsync(id, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        if (refusedFor?.Invoke(survey) is { } code)
        {
            return StatusCode(StatusCodes.Status409Conflict, new PublishProblem(code, [], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var diff = change(survey);
        await AuditAsync(survey, action, cancellationToken, diff);

        if (action == "survey.closed")
        {
            await Askwerk.Api.Integrations.WebhookService.RaiseAsync(context, survey.TenantId, "survey.closed", new { surveyId = survey.Id }, time.GetUtcNow(), cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private Task AuditAsync(Survey survey, string action, CancellationToken cancellationToken, string? diff = null) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), survey.TenantId, AuditActorKind.User, User.UserId().ToString()!, action, "Survey", survey.Id.ToString(), diff, trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>A new survey.</summary>
/// <param name="Title">Internal name.</param>
/// <param name="Languages">Languages offered.</param>
/// <param name="DefaultLanguage">One of the languages.</param>
/// <param name="TemplateId">A template to start from (AWK-17); its languages replace the ones given.</param>
public sealed record CreateSurveyRequest(string? Title, IReadOnlyList<string>? Languages, string? DefaultLanguage, string? TemplateId = null);

/// <summary>The new survey.</summary>
/// <param name="Id">Its id.</param>
public sealed record CreatedSurveyResponse(Guid Id);

/// <summary>A survey's new title.</summary>
/// <param name="Title">The internal title.</param>
public sealed record RenameSurveyRequest(string? Title);

/// <summary>What the survey's bar and tabs show (AWK-29).</summary>
/// <param name="Id">The survey.</param>
/// <param name="Title">The internal title.</param>
/// <param name="Status">draft, published, closed or archived.</param>
/// <param name="PublishedVersion">The latest published version.</param>
/// <param name="HasChanges">Whether the draft differs from the published version.</param>
/// <param name="Languages">The draft's languages.</param>
/// <param name="Responses">Responses so far.</param>
/// <param name="Participants">People invited.</param>
/// <param name="ClosesAt">The closing date (UTC), if any (W24).</param>
/// <param name="TimeZone">The instance's time zone the closing date is entered and shown in.</param>
/// <param name="OneResponsePerPerson">Whether a browser that finished may not answer again.</param>
/// <param name="TemplateId">Set when this survey is where a template is edited (W32): no publishing, no results.</param>
public sealed record SurveySummaryResponse(Guid Id, string Title, string Status, int? PublishedVersion, bool HasChanges, IReadOnlyList<string> Languages, int Responses, int Participants, DateTimeOffset? ClosesAt, string TimeZone, bool OneResponsePerPerson, string? TemplateId = null);

/// <summary>A survey's closing date (W24).</summary>
/// <param name="ClosesAt">Local date and time in the instance's time zone, e.g. 2026-10-31T18:00; null for none.</param>
public sealed record SetClosingDateRequest(string? ClosesAt);

/// <summary>One response per person on or off (W24).</summary>
/// <param name="On">On or off.</param>
public sealed record SetOneResponseRequest(bool On);

/// <summary>One page of the survey list.</summary>
/// <param name="Items">Surveys, last changed first.</param>
/// <param name="Counts">Surveys per status filter: all, draft, published, closed.</param>
/// <param name="Total">How many surveys match the status and search.</param>
public sealed record SurveyListResponse(IReadOnlyList<SurveyListItem> Items, IReadOnlyDictionary<string, int> Counts, int Total);

/// <summary>One row of the survey list (W6).</summary>
/// <param name="Id">The survey.</param>
/// <param name="Title">Internal name.</param>
/// <param name="Status">draft, published, closed or archived.</param>
/// <param name="PublishedVersion">Latest published version, if any.</param>
/// <param name="HasChanges">Whether the draft differs from the published version.</param>
/// <param name="Languages">Languages of the draft.</param>
/// <param name="CreatedBy">Name of the person who created it.</param>
/// <param name="UpdatedAt">Last change (UTC).</param>
/// <param name="Responses">Responses so far, complete and partial (AWK-24).</param>
/// <param name="Front">With cards=true: the front page a card draws (W32).</param>
public sealed record SurveyListItem(Guid Id, string Title, string Status, int? PublishedVersion, bool HasChanges, IReadOnlyList<string> Languages, string CreatedBy, DateTimeOffset UpdatedAt, int Responses, SurveyFront? Front = null);

/// <summary>The draft the designer edits.</summary>
/// <param name="SurveyId">The survey.</param>
/// <param name="Title">Internal name.</param>
/// <param name="VersionNumber">Number the draft gets when published.</param>
/// <param name="Definition">The draft definition.</param>
/// <param name="Published">The last published definition, if any.</param>
/// <param name="ThemeId">The survey's theme; null for the organisation's default (AWK-17).</param>
public sealed record SurveyDraftResponse(Guid SurveyId, string Title, int VersionNumber, JsonElement Definition, JsonElement? Published, Guid? ThemeId);

/// <summary>A draft to save.</summary>
/// <param name="Definition">The definition (packages/schema format).</param>
public sealed record SaveDraftRequest(JsonElement Definition);

/// <summary>The published version.</summary>
/// <param name="VersionNumber">Its number.</param>
/// <param name="Links">Where respondents answer it.</param>
public sealed record PublishedSurveyResponse(int VersionNumber, IReadOnlyList<SurveyLinkResponse> Links);

/// <summary>A link to the survey (AWK-7).</summary>
/// <param name="Kind">system (the instance's address) or domain (a verified domain of the organisation).</param>
/// <param name="Hostname">The address's host.</param>
/// <param name="IsPrimary">Whether it is the organisation's primary address.</param>
/// <param name="Url">The link, /s/{code}.</param>
public sealed record SurveyLinkResponse(string Kind, string Hostname, bool IsPrimary, string Url);

/// <summary>Where a survey is answered.</summary>
/// <param name="Code">Its short code.</param>
/// <param name="Links">Its links, the primary first.</param>
public sealed record SurveyLinksResponse(string Code, IReadOnlyList<SurveyLinkResponse> Links);

/// <summary>Why a survey change was refused.</summary>
/// <param name="Code">publish-checks-failed, role-not-allowed or organisation-not-chosen.</param>
/// <param name="Issues">The findings of the publish checks.</param>
/// <param name="TraceId">Reference for support.</param>
public sealed record PublishProblem(string Code, IReadOnlyList<PublishIssue> Issues, string? TraceId);

/// <summary>One finding of the publish checks.</summary>
/// <param name="Code">Stable code.</param>
/// <param name="Severity">error or warning.</param>
/// <param name="Path">Slash path into the definition.</param>
/// <param name="Language">The language concerned, if any.</param>
public sealed record PublishIssue(string Code, string Severity, string Path, string? Language);

/// <summary>The theme a survey uses.</summary>
/// <param name="ThemeId">A theme of the organisation, or null for the default.</param>
public sealed record SetSurveyThemeRequest(Guid? ThemeId);

/// <summary>The survey's look (W26).</summary>
/// <param name="Tokens">The tokens it is shown with (packages/schema/src/theme.ts).</param>
/// <param name="Own">Whether they are the survey's own look; otherwise its theme's or the organisation's default.</param>
/// <param name="LookBase">The look its own look started from: "preset:konfetti" or "theme:{id}".</param>
/// <param name="ThemeId">The survey's theme, if it picked one.</param>
public sealed record SurveyLookResponse(JsonElement Tokens, bool Own, string? LookBase, Guid? ThemeId);

/// <summary>The survey's own look (W26).</summary>
/// <param name="Tokens">Theme tokens (packages/schema/src/theme.ts).</param>
/// <param name="LookBase">The look it started from: "preset:konfetti" or "theme:{id}"; optional.</param>
public sealed record SetSurveyLookRequest(JsonElement Tokens, string? LookBase);
