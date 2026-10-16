// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text;
using Askwerk.Api.Integrations;
using Askwerk.Application.Setup;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>Public API: the organisation's surveys, to find the one to export (AWK-25, W14 part 2).</summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Authorize(Policy = PublicApi.SurveysRead)]
[Route("api/v1/surveys")]
[Produces("application/json")]
public sealed class PublicSurveysController(AskwerkDbContext context, IInstanceSettingsRepository settings) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    /// <summary>Lists surveys, newest change first.</summary>
    /// <remarks>Scope surveys:read. Pages on a cursor: pass nextCursor as cursor until it is null.</remarks>
    /// <param name="status">draft, published or closed; all when empty.</param>
    /// <param name="limit">Surveys per page, at most 100; 50 when empty.</param>
    /// <param name="cursor">Where the page starts, from the previous page's nextCursor.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of surveys.</returns>
    /// <response code="200">One page of surveys.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks surveys:read.</response>
    /// <response code="429">rate-limited: too many calls; see Retry-After.</response>
    [HttpGet]
    [ProducesResponseType<PublicSurveyPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<PublicSurveyPage>> List(string? status, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var surveys = context.Surveys.Where(survey => survey.DeletedAt == null && survey.Status != SurveyStatus.Archived);

        if (Enum.TryParse<SurveyStatus>(status, ignoreCase: true, out var wanted) && wanted != SurveyStatus.Archived && !int.TryParse(status, out _))
        {
            surveys = surveys.Where(survey => survey.Status == wanted);
        }

        if (ReadCursor(cursor) is { } after)
        {
            surveys = surveys.Where(survey => survey.UpdatedAt < after.At || (survey.UpdatedAt == after.At && survey.Id.CompareTo(after.Id) < 0));
        }

        var rows = await surveys
            .OrderByDescending(survey => survey.UpdatedAt)
            .ThenByDescending(survey => survey.Id)
            .Take(size + 1)
            .Select(survey => new { survey, Responses = context.Responses.Count(response => response.SurveyId == survey.Id) })
            .ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var last = rows.Count > size ? page[^1].survey : null;

        return new PublicSurveyPage(
            [.. page.Select(row => Item(row.survey, row.Responses))],
            last is null ? null : WriteCursor(last.UpdatedAt, last.Id));
    }

    /// <summary>Reads one survey, with its published versions and links.</summary>
    /// <remarks>Scope surveys:read.</remarks>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The survey.</returns>
    /// <response code="200">The survey.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks surveys:read.</response>
    /// <response code="404">survey-not-found: no such survey in the key's organisation.</response>
    /// <response code="429">rate-limited: too many calls; see Retry-After.</response>
    [HttpGet("{surveyId:guid}")]
    [ProducesResponseType<PublicSurvey>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Get(Guid surveyId, CancellationToken cancellationToken)
    {
        if (await context.Surveys.FirstOrDefaultAsync(survey => survey.Id == surveyId && survey.DeletedAt == null, cancellationToken) is not { } survey)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "survey-not-found");
        }

        var responses = await context.Responses.CountAsync(response => response.SurveyId == surveyId, cancellationToken);
        var versions = await context.SurveyVersions
            .Where(version => version.SurveyId == surveyId && version.State != SurveyVersionState.Draft)
            .OrderBy(version => version.Number)
            .Select(version => new PublicSurveyVersion(version.Number, version.PublishedAt))
            .ToListAsync(cancellationToken);
        var links = survey.PublishedVersion is null ? [] : (await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken)).Select(link => link.Url).ToList();
        var item = Item(survey, responses);

        return Ok(new PublicSurvey(item.Id, item.Code, item.Title, item.Status, item.PublishedVersion, item.Languages, item.Responses, item.UpdatedAt, survey.DefaultLanguage, versions, links));
    }

    private static PublicSurveyItem Item(Survey survey, int responses) =>
        new(survey.Id, survey.Code, survey.Title, survey.Status.ToString().ToLowerInvariant(), survey.PublishedVersion, survey.Languages, responses, survey.UpdatedAt);

    private static string WriteCursor(DateTimeOffset updatedAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{updatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (DateTimeOffset At, Guid Id)? ReadCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='))).Split(':');

            return (new DateTimeOffset(long.Parse(parts[0], CultureInfo.InvariantCulture), TimeSpan.Zero), Guid.ParseExact(parts[1], "N"));
        }
        catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>A survey in the list.</summary>
/// <param name="Id">The survey's id.</param>
/// <param name="Code">The short code of its link (/s/{code}).</param>
/// <param name="Title">Its internal title.</param>
/// <param name="Status">draft, published or closed.</param>
/// <param name="PublishedVersion">The latest published version; null before publishing.</param>
/// <param name="Languages">Its languages, e.g. de and en.</param>
/// <param name="Responses">Responses so far, complete and partial.</param>
/// <param name="UpdatedAt">Last change (UTC).</param>
public sealed record PublicSurveyItem(Guid Id, string Code, string Title, string Status, int? PublishedVersion, IReadOnlyList<string> Languages, int Responses, DateTimeOffset UpdatedAt);

/// <summary>One page of surveys.</summary>
/// <param name="Items">Newest change first.</param>
/// <param name="NextCursor">The cursor of the next page; null on the last.</param>
public sealed record PublicSurveyPage(IReadOnlyList<PublicSurveyItem> Items, string? NextCursor);

/// <summary>A published version.</summary>
/// <param name="Number">1, 2, 3 …</param>
/// <param name="PublishedAt">Published (UTC).</param>
public sealed record PublicSurveyVersion(int Number, DateTimeOffset? PublishedAt);

/// <summary>One survey.</summary>
/// <param name="Id">The survey's id.</param>
/// <param name="Code">The short code of its link.</param>
/// <param name="Title">Its internal title.</param>
/// <param name="Status">draft, published or closed.</param>
/// <param name="PublishedVersion">The latest published version.</param>
/// <param name="Languages">Its languages.</param>
/// <param name="Responses">Responses so far.</param>
/// <param name="UpdatedAt">Last change (UTC).</param>
/// <param name="DefaultLanguage">The language a respondent starts in.</param>
/// <param name="Versions">The published versions, oldest first.</param>
/// <param name="Links">Where respondents open it, primary first; empty before publishing.</param>
public sealed record PublicSurvey(Guid Id, string Code, string Title, string Status, int? PublishedVersion, IReadOnlyList<string> Languages, int Responses, DateTimeOffset UpdatedAt, string DefaultLanguage, IReadOnlyList<PublicSurveyVersion> Versions, IReadOnlyList<string> Links);
