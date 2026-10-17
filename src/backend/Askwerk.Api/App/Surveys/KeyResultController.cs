// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>
/// One survey's key result (W37, &lt;askwerk-result&gt;): the card's key result on its own, for a widget in a host app.
/// A question variable picks the question; the survey's key question otherwise.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys/{id:guid}/key-result")]
public sealed class KeyResultController(AskwerkDbContext context) : ControllerBase
{
    /// <summary>The survey's key result, live.</summary>
    /// <param name="id">The survey.</param>
    /// <param name="question">A question's variable, e.g. SAT; the key question when empty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result; 404 when the survey or question is not found.</returns>
    [HttpGet]
    [ProducesResponseType<SurveyKeyResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, string? question, CancellationToken cancellationToken)
    {
        var row = await context.Surveys
            .Where(survey => survey.Id == id && survey.DeletedAt == null)
            .Select(survey => new
            {
                survey.Title,
                survey.PublishedVersion,
                Published = survey.PublishedVersion != null ? context.SurveyVersions.Where(version => version.SurveyId == survey.Id && version.Number == survey.PublishedVersion).Select(version => version.Definition).FirstOrDefault() : null,
                Draft = context.SurveyVersions.Where(version => version.SurveyId == survey.Id && version.State == SurveyVersionState.Draft).Select(version => version.Definition).FirstOrDefault(),
                Responses = context.Responses.Count(response => response.SurveyId == survey.Id),
                Complete = context.Responses.Count(response => response.SurveyId == survey.Id && response.CompletedAt != null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || (row.Published ?? row.Draft) is not { } json)
        {
            return NotFound();
        }

        var definition = JsonSerializer.Deserialize<SurveyDefinition>(json, SurveyDefinitionJson.Options)!;

        if (!string.IsNullOrWhiteSpace(question))
        {
            if (!definition.Pages.SelectMany(page => page.Questions).Any(each => string.Equals(each.Variable, question, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            // The chosen question becomes the only key question, so the card's own reckoning applies.
            definition = definition with
            {
                Pages = [.. definition.Pages.Select(page => page with { Questions = [.. page.Questions.Select(each => each with { Key = string.Equals(each.Variable, question, StringComparison.OrdinalIgnoreCase) })] })],
            };
        }

        var keys = await SurveyKeyResults.LoadAsync(context, [(id, definition, row.Published is not null)], cancellationToken);

        return Ok(new SurveyKeyResult(row.Title, row.Responses, row.Complete, keys.GetValueOrDefault(id) is { Count: > 0 } found ? found[0] : null));
    }
}

/// <summary>One survey's key result.</summary>
/// <param name="Title">The survey's internal title.</param>
/// <param name="Responses">All responses.</param>
/// <param name="Complete">Complete responses.</param>
/// <param name="Key">The key result; null when the survey has no question to show.</param>
public sealed record SurveyKeyResult(string Title, int Responses, int Complete, KeyResult? Key);
