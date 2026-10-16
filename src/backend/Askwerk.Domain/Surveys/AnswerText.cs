// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;

namespace Askwerk.Domain.Surveys;

/// <summary>
/// Answers as people read them (AWK-24): option labels instead of codes, in one language. Results show them; exports
/// with labels write them.
/// </summary>
public static class AnswerText
{
    private static readonly TranslatedText DontKnow = new(new Dictionary<string, string> { ["de"] = "Weiß nicht", ["en"] = "Don't know" });
    private static readonly TranslatedText DefaultYes = new(new Dictionary<string, string> { ["de"] = "Ja", ["en"] = "Yes" });
    private static readonly TranslatedText DefaultNo = new(new Dictionary<string, string> { ["de"] = "Nein", ["en"] = "No" });

    /// <summary>A text in the language, else in the survey's default language, else in English.</summary>
    /// <param name="text">The translations.</param>
    /// <param name="language">The language wanted.</param>
    /// <param name="fallback">The survey's default language.</param>
    /// <returns>The text; empty when there is none.</returns>
    public static string In(TranslatedText? text, string language, string fallback) =>
        text is null ? ""
        : text.Has(language) ? text[language]
        : text.Has(fallback) ? text[fallback]
        : text.Has("en") ? text["en"]
        : text.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    /// <summary>The options a question counts in its results, in the survey's order: options, then "Other".</summary>
    /// <param name="question">The question.</param>
    /// <returns>Code and label of each option; empty for questions without options.</returns>
    public static IReadOnlyList<(string Code, TranslatedText Label)> Options(Question question)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (question.Type == QuestionType.YesNo)
        {
            return [(question.Yes?.Code ?? "1", question.Yes?.Label ?? DefaultYes), (question.No?.Code ?? "0", question.No?.Label ?? DefaultNo)];
        }

        var options = (question.Options ?? []).Select(option => (option.Code, option.Label)).ToList();

        if (question.Other is { } other)
        {
            options.Add((other.Code, other.Label));
        }

        return options;
    }

    /// <summary>An answer as one line of text, e.g. "Search, Other: trade fair".</summary>
    /// <param name="question">The question answered.</param>
    /// <param name="value">The answer.</param>
    /// <param name="language">The language for labels.</param>
    /// <param name="fallback">The survey's default language.</param>
    /// <returns>The text; null when nothing was answered.</returns>
    public static string? Format(Question question, AnswerValue value, string language, string fallback)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(value);

        if (value.DontKnow == true)
        {
            return In(DontKnow, language, fallback);
        }

        var labels = Options(question).ToDictionary(option => option.Code, option => In(option.Label, language, fallback), StringComparer.Ordinal);
        string Label(string code) =>
            question.Other is { } other && code == other.Code && !string.IsNullOrWhiteSpace(value.Other)
                ? $"{labels.GetValueOrDefault(code, code)} {value.Other}".Trim()
                : labels.GetValueOrDefault(code, code);

        var text = question.Type switch
        {
            QuestionType.MultipleChoice => value.Options is { Count: > 0 } chosen ? string.Join(", ", chosen.Select(Label)) : null,
            QuestionType.Ranking => value.Order is { Count: > 0 } order ? string.Join(", ", order.Select((code, index) => $"{index + 1}. {Label(code)}")) : null,
            QuestionType.Matrix => value.Rows is { Count: > 0 } rows ? string.Join("; ", Rows(question, rows, language, fallback).Select(row => $"{row.Row}: {row.Value}")) : null,
            _ when value.Option is not null => Label(value.Option),
            _ when value.Number is { } number => Number(number),
            _ when !string.IsNullOrWhiteSpace(value.Date) => value.Date,
            _ => string.IsNullOrWhiteSpace(value.Text) ? null : value.Text,
        };

        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>The answered rows of a matrix question, in the survey's order, as labels.</summary>
    /// <param name="question">The matrix question.</param>
    /// <param name="rows">Row code to option code.</param>
    /// <param name="language">The language for labels.</param>
    /// <param name="fallback">The survey's default language.</param>
    /// <returns>Row label and answer label per answered row.</returns>
    public static IEnumerable<(string Row, string Value)> Rows(Question question, IReadOnlyDictionary<string, string> rows, string language, string fallback)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(rows);
        var labels = (question.Options ?? []).ToDictionary(option => option.Code, option => In(option.Label, language, fallback), StringComparer.Ordinal);

        foreach (var row in question.Rows ?? [])
        {
            if (rows.TryGetValue(row.Code, out var code))
            {
                yield return (In(row.Label, language, fallback), labels.GetValueOrDefault(code, code));
            }
        }
    }

    /// <summary>A number without trailing zeros, with a dot.</summary>
    /// <param name="number">The number.</param>
    /// <returns>E.g. "4" or "2.5".</returns>
    public static string Number(decimal number) => number.ToString("0.############", CultureInfo.InvariantCulture);
}
