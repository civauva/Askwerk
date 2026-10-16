// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Themes;

/// <summary>Which look a survey has (AWK-17, W26): its own look, else its theme, else the organisation's default, else Werkstatt.</summary>
public static class SurveyThemes
{
    /// <summary>The tokens the survey is shown with.</summary>
    /// <param name="context">A context in the survey's organisation.</param>
    /// <param name="survey">The survey.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tokens.</returns>
    public static async Task<ThemeTokens> EffectiveAsync(AskwerkDbContext context, Survey survey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(survey);

        if (survey.Look is { } own && ThemeTokensJson.Read(own) is { } ownTokens)
        {
            return ownTokens;
        }

        var stored = await context.Themes
            .Where(theme => theme.Id == survey.ThemeId || theme.IsDefault)
            .OrderByDescending(theme => theme.Id == survey.ThemeId)
            .Select(theme => theme.Tokens)
            .FirstOrDefaultAsync(cancellationToken);

        return stored is not null && ThemeTokensJson.Read(stored) is { } tokens ? tokens : DefaultTheme.Werkstatt;
    }

    /// <summary>The tokens as JSON in the shared format.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>JSON for a response.</returns>
    public static JsonElement ToJson(ThemeTokens tokens) => JsonSerializer.SerializeToElement(tokens, SurveyDefinitionJson.Options);
}
