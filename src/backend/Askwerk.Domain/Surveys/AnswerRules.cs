// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text.RegularExpressions;

namespace Askwerk.Domain.Surveys;

/// <summary>
/// One answer as the runner sends it (AWK-6). Which fields are used depends on the question type: option (single,
/// dropdown, yes/no), options (multiple), other (the "Other" text), number (rating, NPS, slider, number), text,
/// date (date, date-time or time, as written for the mode), rows (matrix: row code → option code), order (ranking).
/// </summary>
/// <param name="Option">The chosen option code.</param>
/// <param name="Options">The chosen option codes.</param>
/// <param name="Other">Text of "Other, please specify".</param>
/// <param name="Number">A number.</param>
/// <param name="Text">A text.</param>
/// <param name="Date">"2026-10-14", "2026-10-14T09:30" or "09:30".</param>
/// <param name="Rows">Matrix answers per row code.</param>
/// <param name="Order">Option codes in ranked order.</param>
/// <param name="DontKnow">The respondent chose "Don't know".</param>
public sealed record AnswerValue(
    string? Option = null,
    IReadOnlyList<string>? Options = null,
    string? Other = null,
    decimal? Number = null,
    string? Text = null,
    string? Date = null,
    IReadOnlyDictionary<string, string>? Rows = null,
    IReadOnlyList<string>? Order = null,
    bool? DontKnow = null)
{
    /// <summary>Whether nothing was answered.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Option) && (Options is null || Options.Count == 0) && Number is null && string.IsNullOrWhiteSpace(Text)
        && string.IsNullOrWhiteSpace(Date) && (Rows is null || Rows.Count == 0) && (Order is null || Order.Count == 0) && DontKnow != true;
}

/// <summary>The checks every answer passes on the server before it is stored (W8, note 1).</summary>
public static partial class AnswerRules
{
    /// <summary>Longest text answer.</summary>
    public const int MaxTextLength = 2000;

    /// <summary>The problems on one page, by question id; empty when the page can be stored. Text blocks are skipped.</summary>
    /// <param name="page">The page, from the definition.</param>
    /// <param name="answers">Answers by question id.</param>
    /// <param name="today">Today's date, for "today" in earliest and latest.</param>
    /// <returns>Question id → problem code.</returns>
    public static IReadOnlyDictionary<string, string> CheckPage(Page page, IReadOnlyDictionary<string, AnswerValue> answers, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(answers);

        return page.Questions
            .Where(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image))
            .Select(question => (question.Id, Problem: Check(question, answers.GetValueOrDefault(question.Id), today)))
            .Where(found => found.Problem is not null)
            .ToDictionary(found => found.Id, found => found.Problem!);
    }

    /// <summary>The first problem with an answer, or null when it can be stored.</summary>
    /// <param name="question">The question, from the published definition.</param>
    /// <param name="value">The answer; null when none was given.</param>
    /// <param name="today">Today's date, for "today" in earliest and latest.</param>
    /// <returns>A code such as "required" or "format-email"; null when fine.</returns>
    public static string? Check(Question question, AnswerValue? value, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (value is null || value.IsEmpty)
        {
            return question.Required ? "required" : null;
        }

        if (value.DontKnow == true)
        {
            return question.DontKnow == true ? null : "answer-invalid";
        }

        return question.Type switch
        {
            QuestionType.Rating => CheckScale(value, question.Scale?.Min ?? 1, question.Scale?.Max ?? 5, 1),
            QuestionType.Nps => CheckScale(value, 0, 10, 1),
            QuestionType.Slider => CheckScale(value, question.Scale?.Min ?? 0, question.Scale?.Max ?? 100, question.Scale?.Step ?? 1),
            QuestionType.Number => value.Number is null ? "answer-invalid" : null,
            QuestionType.SingleChoice or QuestionType.Dropdown => CheckOne(question, value, ChoiceCodes(question)),
            QuestionType.YesNo => CheckOne(question, value, [question.Yes?.Code ?? "1", question.No?.Code ?? "0"]),
            QuestionType.MultipleChoice => CheckMany(question, value),
            QuestionType.ShortText or QuestionType.LongText => CheckText(question, value),
            QuestionType.Date => CheckDate(question, value, today),
            QuestionType.Matrix => CheckMatrix(question, value),
            QuestionType.Ranking => CheckRanking(question, value),
            _ => "answer-invalid",
        };
    }

    private static string? CheckScale(AnswerValue value, int min, int max, int step)
    {
        if (value.Number is not { } number)
        {
            return "answer-invalid";
        }

        if (number != decimal.Truncate(number))
        {
            return "number-not-whole";
        }

        if (number < min || number > max)
        {
            return "number-out-of-range";
        }

        return (number - min) % step == 0 ? null : "number-off-step";
    }

    private static HashSet<string> ChoiceCodes(Question question)
    {
        var codes = (question.Options ?? []).Select(option => option.Code).ToHashSet(StringComparer.Ordinal);

        if (question.Other is { } other)
        {
            codes.Add(other.Code);
        }

        return codes;
    }

    private static string? CheckOne(Question question, AnswerValue value, IReadOnlyCollection<string> codes)
    {
        if (string.IsNullOrWhiteSpace(value.Option))
        {
            return "answer-invalid";
        }

        if (!codes.Contains(value.Option))
        {
            return "option-unknown";
        }

        return question.Other?.Code == value.Option && string.IsNullOrWhiteSpace(value.Other) ? "other-text-missing" : null;
    }

    private static string? CheckMany(Question question, AnswerValue value)
    {
        var chosen = value.Options ?? [];
        var codes = ChoiceCodes(question);

        if (chosen.Count == 0)
        {
            return question.Required ? "required" : "answer-invalid";
        }

        if (chosen.Any(code => !codes.Contains(code)) || chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Count)
        {
            return "option-unknown";
        }

        return question.Other is { } other && chosen.Contains(other.Code) && string.IsNullOrWhiteSpace(value.Other) ? "other-text-missing" : null;
    }

    private static string? CheckText(Question question, AnswerValue value)
    {
        if (value.Text is null)
        {
            return "answer-invalid";
        }

        var text = value.Text.Trim();

        if (text.Length == 0)
        {
            return question.Required ? "required" : null;
        }

        if (text.Length > MaxTextLength)
        {
            return "text-too-long";
        }

        return question.Format switch
        {
            TextFormat.Email when !EmailPattern().IsMatch(text) => "format-email",
            TextFormat.Phone when !PhonePattern().IsMatch(text) || text.Count(char.IsAsciiDigit) < 6 => "format-phone",
            TextFormat.Url when !UrlPattern().IsMatch(text) => "format-url",
            _ => null,
        };
    }

    private static string? CheckDate(Question question, AnswerValue value, DateOnly today)
    {
        var mode = question.Mode ?? DateMode.Date;
        var text = value.Date?.Trim() ?? "";
        var valid = mode switch
        {
            DateMode.DateTime => DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            DateMode.Time => TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        };

        if (!valid)
        {
            return "date-invalid";
        }

        string Resolve(string limit) => limit == "today" ? today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : limit;

        // Values written for the mode compare correctly as text: ISO dates and 24-hour times sort in order.
        if (question.Earliest is { } earliest && string.CompareOrdinal(text, Resolve(earliest)) < 0)
        {
            return "date-too-early";
        }

        if (question.Latest is { } latest)
        {
            var bound = Resolve(latest);

            if (mode == DateMode.DateTime && latest == "today")
            {
                bound += "T23:59";
            }

            if (string.CompareOrdinal(text, bound) > 0)
            {
                return "date-too-late";
            }
        }

        return null;
    }

    private static string? CheckMatrix(Question question, AnswerValue value)
    {
        var answers = value.Rows ?? new Dictionary<string, string>();
        var rows = (question.Rows ?? []).Select(row => row.Code).ToHashSet(StringComparer.Ordinal);
        var options = (question.Options ?? []).Select(option => option.Code).ToHashSet(StringComparer.Ordinal);

        if (answers.Any(answer => !rows.Contains(answer.Key) || !options.Contains(answer.Value)))
        {
            return "option-unknown";
        }

        return question.Required && rows.Any(row => !answers.ContainsKey(row)) ? "rows-missing" : null;
    }

    private static string? CheckRanking(Question question, AnswerValue value)
    {
        var order = value.Order ?? [];
        var codes = (question.Options ?? []).Select(option => option.Code).ToList();

        return order.Count == codes.Count && order.ToHashSet(StringComparer.Ordinal).SetEquals(codes) ? null : "order-invalid";
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ()./-]{6,24}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^(https?://)?[^\s/.]+(\.[^\s/.]+)+(/\S*)?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
