// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.App.Themes;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Surveys;

/// <summary>
/// What a card in the Surveys and Templates lists draws (W32): the welcome page's title and text in the survey's look,
/// not a screenshot. The organisation's looks are read once per list.
/// </summary>
internal sealed class SurveyFronts
{
    private readonly Dictionary<Guid, ThemeTokens> themes;
    private readonly ThemeTokens fallback;

    private SurveyFronts(Dictionary<Guid, ThemeTokens> themes, ThemeTokens fallback)
    {
        this.themes = themes;
        this.fallback = fallback;
    }

    /// <summary>Reads the organisation's looks.</summary>
    /// <param name="context">A context in the user's organisation.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The fronts.</returns>
    public static async Task<SurveyFronts> LoadAsync(AskwerkDbContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Themes.Select(theme => new { theme.Id, theme.IsDefault, theme.Tokens }).ToListAsync(cancellationToken);
        var themes = new Dictionary<Guid, ThemeTokens>();
        ThemeTokens? fallback = null;

        foreach (var row in rows)
        {
            if (ThemeTokensJson.Read(row.Tokens) is { } tokens)
            {
                themes[row.Id] = tokens;
                fallback = row.IsDefault ? tokens : fallback;
            }
        }

        return new SurveyFronts(themes, fallback ?? DefaultTheme.Werkstatt);
    }

    /// <summary>The front of a survey or template.</summary>
    /// <param name="definition">The definition, or null for none.</param>
    /// <param name="look">Its own look as tokens JSON, if any.</param>
    /// <param name="themeId">Its theme, if any.</param>
    /// <returns>The front.</returns>
    public SurveyFront Of(SurveyDefinition? definition, string? look, Guid? themeId)
    {
        var tokens = look is not null && ThemeTokensJson.Read(look) is { } own ? own : themeId is { } id && themes.TryGetValue(id, out var theme) ? theme : fallback;

        return new SurveyFront(definition?.Welcome?.Title, definition?.Welcome?.Text, definition?.DefaultLanguage ?? "", SurveyThemes.ToJson(tokens), definition?.HeaderImage?.Asset);
    }
}

/// <summary>The front page a card draws (W32).</summary>
/// <param name="Title">The welcome page's title per language, if any.</param>
/// <param name="Text">The welcome page's text per language, if any.</param>
/// <param name="DefaultLanguage">The language to fall back to.</param>
/// <param name="Look">The look's tokens (packages/schema/src/theme.ts).</param>
/// <param name="HeaderImage">The survey's own header picture, if any; it wins over the look's.</param>
public sealed record SurveyFront(TranslatedText? Title, TranslatedText? Text, string DefaultLanguage, JsonElement Look, string? HeaderImage = null);
