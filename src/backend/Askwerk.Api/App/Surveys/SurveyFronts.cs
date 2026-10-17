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
    private readonly Dictionary<Guid, (ThemeTokens Tokens, string Name)> themes;
    private readonly (ThemeTokens Tokens, string? Name) fallback;

    private SurveyFronts(Dictionary<Guid, (ThemeTokens Tokens, string Name)> themes, (ThemeTokens Tokens, string? Name) fallback)
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
        var rows = await context.Themes.Select(theme => new { theme.Id, theme.IsDefault, theme.Tokens, theme.Name }).ToListAsync(cancellationToken);
        var themes = new Dictionary<Guid, (ThemeTokens Tokens, string Name)>();
        (ThemeTokens Tokens, string? Name)? fallback = null;

        foreach (var row in rows)
        {
            if (ThemeTokensJson.Read(row.Tokens) is { } tokens)
            {
                themes[row.Id] = (tokens, row.Name);
                fallback = row.IsDefault ? (tokens, row.Name) : fallback;
            }
        }

        // Without a look of its own, an organisation's surveys look like Askwerk's Werkstatt.
        return new SurveyFronts(themes, fallback ?? (DefaultTheme.Werkstatt, null));
    }

    /// <summary>The front of a survey or template.</summary>
    /// <param name="definition">The definition, or null for none.</param>
    /// <param name="look">Its own look as tokens JSON, if any.</param>
    /// <param name="themeId">Its theme, if any.</param>
    /// <param name="lookBase">Where its own look started: "preset:konfetti" or "theme:{id}".</param>
    /// <returns>The front, with the name of its look a card shows (W32): a theme's name, or the Askwerk look it came from.</returns>
    public SurveyFront Of(SurveyDefinition? definition, string? look, Guid? themeId, string? lookBase = null)
    {
        ThemeTokens tokens;
        string? name = null;
        string? preset = null;

        if (look is not null && ThemeTokensJson.Read(look) is { } own)
        {
            tokens = own;
            name = lookBase is not null && lookBase.StartsWith("theme:", StringComparison.Ordinal) && Guid.TryParse(lookBase["theme:".Length..], out var baseId) && themes.TryGetValue(baseId, out var based) ? based.Name : null;
            preset = lookBase is not null && lookBase.StartsWith("preset:", StringComparison.Ordinal) ? lookBase["preset:".Length..] : null;
        }
        else if (themeId is { } id && themes.TryGetValue(id, out var theme))
        {
            (tokens, name) = theme;
        }
        else
        {
            (tokens, name) = fallback;
            preset = name is null ? "werkstatt" : null;
        }

        return new SurveyFront(definition?.Welcome?.Title, definition?.Welcome?.Text, definition?.DefaultLanguage ?? "", SurveyThemes.ToJson(tokens), definition?.HeaderImage?.Asset, Contents(definition), LookName: name, LookPreset: preset);
    }

    /// <summary>What a survey asks (W32 card A): its size, about how long it takes, the first questions and the types.</summary>
    /// <param name="definition">The definition, or null.</param>
    /// <returns>The contents; null without a definition.</returns>
    public static SurveyContents? Contents(SurveyDefinition? definition)
    {
        if (definition is null)
        {
            return null;
        }

        // As the runner counts them: text blocks and pictures are not questions.
        var questions = definition.Pages.SelectMany(page => page.Questions).Where(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image)).ToList();
        var types = questions.GroupBy(question => TypeName(question.Type)).OrderByDescending(group => group.Count()).ToDictionary(group => group.Key, group => group.Count());

        return new SurveyContents(
            questions.Count,
            definition.Pages.Count,
            Math.Max(1, (int)Math.Round(questions.Count * 0.3, MidpointRounding.AwayFromZero)),
            [.. questions.Take(3).Select(question => new SurveyQuestionLine(question.Text, TypeName(question.Type)))],
            types);
    }

    /// <summary>The type as the definition's JSON names it: single, rating, nps, short-text…</summary>
    private static string TypeName(QuestionType type) => JsonSerializer.Serialize(type, SurveyDefinitionJson.Options).Trim('"');
}

/// <summary>What a survey asks (W32).</summary>
/// <param name="Questions">Questions, without text blocks and pictures.</param>
/// <param name="Pages">Pages.</param>
/// <param name="Minutes">About how many minutes answering takes, as the welcome page says.</param>
/// <param name="First">The first three questions.</param>
/// <param name="Types">How many questions of each type, most first.</param>
public sealed record SurveyContents(int Questions, int Pages, int Minutes, IReadOnlyList<SurveyQuestionLine> First, IReadOnlyDictionary<string, int> Types);

/// <summary>One question on a card (W32).</summary>
/// <param name="Text">Its text per language.</param>
/// <param name="Type">Its type as in the definition.</param>
public sealed record SurveyQuestionLine(TranslatedText Text, string Type);

/// <summary>How answering goes (W32): responses, the share complete, the last one, and the last 14 days.</summary>
/// <param name="Complete">Complete responses.</param>
/// <param name="Partial">Started, not finished.</param>
/// <param name="LastAt">The last response's last change (UTC), if any.</param>
/// <param name="Days">Responses started on each of the last 14 days, oldest first, today last.</param>
public sealed record SurveyAnswers(int Complete, int Partial, DateTimeOffset? LastAt, IReadOnlyList<int> Days);

/// <summary>The front page a card draws (W32).</summary>
/// <param name="Title">The welcome page's title per language, if any.</param>
/// <param name="Text">The welcome page's text per language, if any.</param>
/// <param name="DefaultLanguage">The language to fall back to.</param>
/// <param name="Look">The look's tokens (packages/schema/src/theme.ts).</param>
/// <param name="HeaderImage">The survey's own header picture, if any; it wins over the look's.</param>
/// <param name="Contents">What it asks (card A).</param>
/// <param name="Answers">Surveys only: how answering goes.</param>
/// <param name="Code">Surveys only: the code in its link, /s/{code}.</param>
/// <param name="PublishedAt">Surveys only: when the version now published was published (UTC).</param>
/// <param name="Keys">Surveys only: the key questions' results (W38), in survey order.</param>
/// <param name="LookName">The name of its look when that is one of the organisation's looks.</param>
/// <param name="LookPreset">The Askwerk look it is or started from ("konfetti"); the card names it in the reader's language.</param>
public sealed record SurveyFront(TranslatedText? Title, TranslatedText? Text, string DefaultLanguage, JsonElement Look, string? HeaderImage = null, SurveyContents? Contents = null, SurveyAnswers? Answers = null, string? Code = null, DateTimeOffset? PublishedAt = null, IReadOnlyList<KeyResult>? Keys = null, string? LookName = null, string? LookPreset = null);
