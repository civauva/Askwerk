// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Integrations;
using Askwerk.Application.Exports;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Results;

/// <summary>
/// Public API: a survey's responses changed since a sequence, in long format, and its codebook (AWK-14, C14 1). A sync
/// job stores nextSince and asks again from there, so it never misses a change and repeats nothing.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Authorize(Policy = PublicApi.ResponsesRead)]
[Route("api/v1/surveys/{surveyId:guid}")]
[Produces("application/json")]
public sealed class PublicResponsesController(AskwerkDbContext context) : ControllerBase
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 500;

    /// <summary>Lists the survey's responses changed after a sequence, oldest change first.</summary>
    /// <remarks>
    /// Scope responses:read. Every new answer, change and completion gives a response a higher changeSequence; pass
    /// nextSince as since to go on. Answers are in long format, one entry per variable of the codebook, with the value
    /// and its label in the chosen language; an option of multiple choice appears only when chosen.
    /// </remarks>
    /// <param name="surveyId">The survey.</param>
    /// <param name="since">The last changeSequence already stored; 0 for all.</param>
    /// <param name="limit">At most 500; 200 when not given.</param>
    /// <param name="language">Labels in this survey language; the default language when not given.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of responses.</returns>
    /// <response code="200">A page of responses.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks responses:read.</response>
    /// <response code="404">survey-not-found.</response>
    [HttpGet("responses")]
    [ProducesResponseType<PublicResponsePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Responses(Guid surveyId, long? since, int? limit, string? language, CancellationToken cancellationToken)
    {
        if (!await context.Surveys.AnyAsync(survey => survey.Id == surveyId && survey.DeletedAt == null, cancellationToken))
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "survey-not-found");
        }

        var after = Math.Max(0, since ?? 0);
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var responses = await context.Responses.AsNoTracking()
            .Where(response => response.SurveyId == surveyId && response.ChangeSeq > after)
            .OrderBy(response => response.ChangeSeq)
            .Take(size)
            .ToListAsync(cancellationToken);

        if (responses.Count == 0)
        {
            return Ok(new PublicResponsePage([], after));
        }

        var ids = responses.Select(response => response.Id).ToList();
        var versionIds = responses.Select(response => response.SurveyVersionId).Distinct().ToList();
        var versions = await context.SurveyVersions.AsNoTracking().Where(version => versionIds.Contains(version.Id)).ToDictionaryAsync(version => version.Id, cancellationToken);
        var questionKeys = await context.SurveyQuestions.AsNoTracking().Where(question => versionIds.Contains(question.SurveyVersionId)).ToDictionaryAsync(question => question.Id, question => question.QuestionKey, cancellationToken);
        var answers = (await context.Answers.AsNoTracking().Where(answer => ids.Contains(answer.ResponseId)).ToListAsync(cancellationToken)).ToLookup(answer => answer.ResponseId);
        var participantIds = responses.Where(response => response.ParticipantId is not null).Select(response => response.ParticipantId!.Value).ToList();
        var emails = await context.Participants.AsNoTracking().Where(participant => participantIds.Contains(participant.Id)).ToDictionaryAsync(participant => participant.Id, participant => participant.Email, cancellationToken);
        var codebooks = new Dictionary<Guid, (Domain.Surveys.Codebook Codebook, SurveyDefinition Definition)>();

        var items = new List<PublicResponse>();

        foreach (var response in responses)
        {
            if (!codebooks.TryGetValue(response.SurveyVersionId, out var book))
            {
                var definition = versions[response.SurveyVersionId].ReadDefinition();
                book = (Domain.Surveys.Codebook.Of(definition, definition.Languages.Contains(language ?? "") ? language! : definition.DefaultLanguage), definition);
                codebooks[response.SurveyVersionId] = book;
            }

            var questions = book.Definition.Pages.SelectMany(page => page.Questions).ToDictionary(question => question.Id);
            var values = new Dictionary<string, AnswerValue>(StringComparer.Ordinal);

            foreach (var answer in answers[response.Id])
            {
                if (questionKeys.TryGetValue(answer.QuestionId, out var key) && questions.TryGetValue(key, out var question))
                {
                    values[key] = answer.ToValue(question.Type);
                }
            }

            var longFormat = book.Codebook.Variables
                .Select(variable => (variable, Code: book.Codebook.Cell(variable, values.GetValueOrDefault(variable.QuestionKey), labels: false), Label: book.Codebook.Cell(variable, values.GetValueOrDefault(variable.QuestionKey), labels: true)))
                .Where(cell => cell.Code is not null && !(cell.variable.Part == VariablePart.Option && cell.Code == "0"))
                .Select(cell => new PublicAnswer(cell.variable.Name, cell.Code!, cell.Label))
                .ToList();

            items.Add(new PublicResponse(
                response.Id,
                response.Status.ToString().ToLowerInvariant(),
                versions[response.SurveyVersionId].Number,
                response.Language,
                new PublicParticipant(response.ParticipantId is { } participant ? emails.GetValueOrDefault(participant) : response.RespondentEmail, response.RespondentExternalId),
                response.StartedAt,
                response.CompletedAt,
                response.ChangeSeq,
                longFormat));
        }

        return Ok(new PublicResponsePage(items, responses[^1].ChangeSeq));
    }

    /// <summary>The codebook of the survey's latest published version: every variable with its label and values.</summary>
    /// <remarks>Scope responses:read. The variables are those of the responses endpoint and of every export.</remarks>
    /// <param name="surveyId">The survey.</param>
    /// <param name="language">Labels in this survey language; the default language when not given.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The codebook.</returns>
    /// <response code="200">The codebook.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks responses:read.</response>
    /// <response code="404">survey-not-found.</response>
    /// <response code="409">not-published.</response>
    [HttpGet("codebook")]
    [ProducesResponseType<PublicCodebook>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Codebook(Guid surveyId, string? language, CancellationToken cancellationToken)
    {
        if (await context.Surveys.AsNoTracking().FirstOrDefaultAsync(survey => survey.Id == surveyId && survey.DeletedAt == null, cancellationToken) is not { } survey)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "survey-not-found");
        }

        if (survey.PublishedVersion is not { } number
            || await context.SurveyVersions.AsNoTracking().FirstOrDefaultAsync(version => version.SurveyId == surveyId && version.Number == number, cancellationToken) is not { } version)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status409Conflict, "not-published");
        }

        var definition = version.ReadDefinition();
        var chosen = definition.Languages.Contains(language ?? "") ? language! : definition.DefaultLanguage;
        var codebook = Domain.Surveys.Codebook.Of(definition, chosen);

        return Ok(new PublicCodebook(
            number,
            chosen,
            [.. codebook.Variables.Select(variable => new PublicVariable(variable.Name, variable.Label, variable.Numeric, [.. variable.Values.Select(value => new PublicValue(value.Code, value.Label))]))]));
    }
}

/// <summary>A page of responses.</summary>
/// <param name="Items">Oldest change first.</param>
/// <param name="NextSince">Pass as since next time; the same as since when nothing changed.</param>
public sealed record PublicResponsePage(IReadOnlyList<PublicResponse> Items, long NextSince);

/// <summary>A response in long format.</summary>
/// <param name="Id">The response.</param>
/// <param name="Status">partial or complete.</param>
/// <param name="Version">The survey version answered.</param>
/// <param name="Language">The language answered in.</param>
/// <param name="Participant">Who answered, when invited personally.</param>
/// <param name="StartedAt">When it started (UTC).</param>
/// <param name="CompletedAt">When it was sent (UTC).</param>
/// <param name="ChangeSequence">Grows with every change; nextSince is the last of a page.</param>
/// <param name="Answers">One entry per variable with an answer.</param>
public sealed record PublicResponse(Guid Id, string Status, int Version, string Language, PublicParticipant Participant, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long ChangeSequence, IReadOnlyList<PublicAnswer> Answers);

/// <summary>Who answered.</summary>
/// <param name="Email">The invited address; null for an anonymous link.</param>
/// <param name="ExternalId">The host system's user id, when a host product named the respondent (AWK-13).</param>
public sealed record PublicParticipant(string? Email, string? ExternalId);

/// <summary>One variable's answer.</summary>
/// <param name="Variable">As in the codebook, e.g. SAT or SOURCE_2.</param>
/// <param name="Value">The code or number; text for open questions.</param>
/// <param name="Label">The value's label in the chosen language.</param>
public sealed record PublicAnswer(string Variable, string Value, string? Label);

/// <summary>A codebook.</summary>
/// <param name="Version">The published version it describes.</param>
/// <param name="Language">The labels' language.</param>
/// <param name="Variables">Every variable in order.</param>
public sealed record PublicCodebook(int Version, string Language, IReadOnlyList<PublicVariable> Variables);

/// <summary>A variable.</summary>
/// <param name="Variable">Its name.</param>
/// <param name="Label">The question (and option or row) it stands for.</param>
/// <param name="Numeric">Whether its values are numbers.</param>
/// <param name="Values">Its codes with labels.</param>
public sealed record PublicVariable(string Variable, string Label, bool Numeric, IReadOnlyList<PublicValue> Values);

/// <summary>A code and its label.</summary>
/// <param name="Code">The code.</param>
/// <param name="Label">The label.</param>
public sealed record PublicValue(string Code, string Label);
