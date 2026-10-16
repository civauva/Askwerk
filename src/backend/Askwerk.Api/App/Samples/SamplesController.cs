// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;
using Askwerk.Api.App.Themes;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Storage;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Assets;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Askwerk.Infrastructure.Samples;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Samples;

/// <summary>
/// Sample surveys (W26): complete surveys that show what Askwerk can do, each with its own look and sample answers.
/// Creating one makes a closed survey with its answers in Results; Reopen or Duplicate starts real work.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/samples")]
public sealed class SamplesController(AskwerkDbContext context, IFileStorage storage, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Editors = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];

    /// <summary>The samples, in their order, with their look and a question to show on the card.</summary>
    /// <returns>The samples.</returns>
    [HttpGet]
    [ProducesResponseType<SampleListResponse>(StatusCodes.Status200OK)]
    public ActionResult<SampleListResponse> List() =>
        new SampleListResponse([.. SampleSurveys.All.Select(sample =>
        {
            var questions = sample.Definition.Pages.SelectMany(page => page.Questions).ToList();
            var shown = questions.FirstOrDefault(question => question.Options is { Count: > 0 } && question.Type != QuestionType.Ranking) ?? questions[0];

            return new SampleResponse(
                sample.Id,
                sample.Names,
                sample.Kind,
                sample.Features,
                sample.Look,
                SurveyThemes.ToJson(SampleSurveys.Looks[sample.Look]),
                sample.Definition.Languages,
                questions.Count(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image)),
                sample.Responses,
                new SamplePreview(shown.Text, [.. (shown.Options ?? []).Take(4).Select(option => option.Label)]));
        })]);

    /// <summary>
    /// Creates the sample in the organisation: its pictures, its own look, published and closed, with its sample
    /// answers. The title is the sample's name unless one is given.
    /// </summary>
    /// <param name="id">The sample, e.g. "cafe".</param>
    /// <param name="request">The survey's title.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the survey's id; 403 for viewers and analysts; 404 for an unknown sample.</returns>
    [HttpPost("{id}")]
    [ProducesResponseType<CreatedSampleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(string id, CreateSampleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (User.TenantId() is not { } tenantId || !await MayEditAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        if (SampleSurveys.Find(id) is not { } sample)
        {
            return NotFound();
        }

        var userId = User.UserId()!.Value;
        var now = time.GetUtcNow();
        var title = string.IsNullOrWhiteSpace(request.Title) ? sample.Names.GetValueOrDefault("en") ?? sample.Id : request.Title.Trim();
        var survey = await SampleSurveyBuilder.CreateAsync(
            context,
            storage,
            sample,
            Guid.CreateVersion7(now),
            tenantId,
            userId,
            title,
            close: true,
            (created, action) => AuditAsync(created, action, cancellationToken),
            () => unitOfWork.SaveChangesAsync(cancellationToken),
            now,
            cancellationToken);

        return Created($"/api/app/surveys/{survey.Id}/draft", new CreatedSampleResponse(survey.Id));
    }

    private async Task<bool> MayEditAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Editors.Contains(known);
    }

    private Task AuditAsync(Survey survey, string action, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), survey.TenantId, AuditActorKind.User, User.UserId().ToString()!, action, "Survey", survey.Id.ToString(), Diff: null, trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>The samples to choose from.</summary>
/// <param name="Items">In their order.</param>
public sealed record SampleListResponse(IReadOnlyList<SampleResponse> Items);

/// <summary>One sample.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Names">Its name per language.</param>
/// <param name="Kind">What it shows, per language.</param>
/// <param name="Features">What to look for, per language.</param>
/// <param name="Look">Its look's id.</param>
/// <param name="Tokens">Its look's tokens, to draw the card.</param>
/// <param name="Languages">Its languages.</param>
/// <param name="Questions">How many questions it has.</param>
/// <param name="Responses">How many sample answers it comes with.</param>
/// <param name="Preview">A question to show on the card.</param>
public sealed record SampleResponse(string Id, TranslatedText Names, TranslatedText Kind, IReadOnlyDictionary<string, IReadOnlyList<string>> Features, string Look, JsonElement Tokens, IReadOnlyList<string> Languages, int Questions, int Responses, SamplePreview Preview);

/// <summary>A question to show on a sample's card.</summary>
/// <param name="Question">Its text per language.</param>
/// <param name="Choices">Up to four of its options.</param>
public sealed record SamplePreview(TranslatedText Question, IReadOnlyList<TranslatedText> Choices);

/// <summary>A sample to create.</summary>
/// <param name="Title">The survey's title; the sample's name when not given.</param>
public sealed record CreateSampleRequest(string? Title);

/// <summary>The survey made from a sample.</summary>
/// <param name="Id">Its id.</param>
public sealed record CreatedSampleResponse(Guid Id);
