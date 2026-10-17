// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Askwerk.Api.App.Surveys;

/// <summary>
/// The key results on the survey cards (W38): for each survey its key questions, or the first NPS or rating question
/// (else the first choice) when none is flagged, with the main answer and its spread. Counted for a whole page of cards
/// in three queries.
/// </summary>
internal static class SurveyKeyResults
{
    private static readonly QuestionType[] Scales = [QuestionType.Rating, QuestionType.Nps, QuestionType.Slider];
    private static readonly QuestionType[] Choices = [QuestionType.SingleChoice, QuestionType.MultipleChoice, QuestionType.Dropdown, QuestionType.YesNo];

    /// <summary>The questions a card shows: the flagged ones in survey order, else the first scale, else the first choice.</summary>
    /// <param name="definition">The survey's definition.</param>
    /// <returns>The questions and whether they were picked automatically.</returns>
    public static (IReadOnlyList<Question> Questions, bool Auto) KeysOf(SurveyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var questions = definition.Pages.SelectMany(page => page.Questions).Where(question => Scales.Contains(question.Type) || Choices.Contains(question.Type)).ToList();
        var flagged = questions.Where(question => question.Key == true).ToList();

        if (flagged.Count > 0)
        {
            return (flagged, false);
        }

        var first = questions.FirstOrDefault(question => question.Type is QuestionType.Nps or QuestionType.Rating) ?? questions.FirstOrDefault(question => Choices.Contains(question.Type));

        return (first is null ? [] : [first], true);
    }

    /// <summary>The key results of the surveys on a page of cards.</summary>
    /// <param name="context">A context in the user's organisation.</param>
    /// <param name="surveys">Each survey with the definition it is answered in (published, else draft) and whether it collects.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The results per survey; surveys without a key question are left out.</returns>
    public static async Task<Dictionary<Guid, IReadOnlyList<KeyResult>>> LoadAsync(AskwerkDbContext context, IReadOnlyList<(Guid SurveyId, SurveyDefinition Definition, bool Published)> surveys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(surveys);
        var published = surveys.Where(survey => survey.Published).Select(survey => survey.SurveyId).ToArray();
        var values = new List<ValueRow>();
        var options = new List<OptionRow>();
        var answered = new List<AnsweredRow>();

        if (published.Length > 0)
        {
            const string From = """

                FROM answer a JOIN survey_question q ON q.id = a.question_id JOIN response r ON r.id = a.response_id
                """;
            const string Where = " WHERE r.survey_id = ANY(@surveys)";
            NpgsqlParameter Surveys() => new("surveys", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = published };

            values = await context.Database.SqlQueryRaw<ValueRow>(
                """SELECT r.survey_id AS "Survey", q.question_key AS "Key", a.number_value::float8 AS "Value", count(*)::int AS "Count" """ + From + Where +
                " AND q.type IN ('rating', 'nps', 'slider') AND a.number_value IS NOT NULL GROUP BY 1, 2, 3",
                Surveys()).ToListAsync(cancellationToken);
            options = await context.Database.SqlQueryRaw<OptionRow>(
                """SELECT r.survey_id AS "Survey", q.question_key AS "Key", o.code AS "Code", count(*)::int AS "Count" """ + From +
                " CROSS JOIN LATERAL unnest(a.option_codes) AS o(code)" + Where + " AND q.type IN ('single', 'multiple', 'dropdown', 'yes-no') GROUP BY 1, 2, 3",
                Surveys()).ToListAsync(cancellationToken);
            answered = await context.Database.SqlQueryRaw<AnsweredRow>(
                """SELECT r.survey_id AS "Survey", q.question_key AS "Key", count(DISTINCT a.id)::int AS "Count" """ + From + Where + " GROUP BY 1, 2",
                Surveys()).ToListAsync(cancellationToken);
        }

        var result = new Dictionary<Guid, IReadOnlyList<KeyResult>>();

        foreach (var (surveyId, definition, isPublished) in surveys)
        {
            var (keys, auto) = KeysOf(definition);

            if (keys.Count > 0)
            {
                result[surveyId] = [.. keys.Select(question => Result(question, auto, isPublished, values, options, answered, surveyId))];
            }
        }

        return result;
    }

    private static KeyResult Result(Question question, bool auto, bool published, List<ValueRow> values, List<OptionRow> options, List<AnsweredRow> answered, Guid surveyId)
    {
        var type = JsonSerializer.Serialize(question.Type, SurveyDefinitionJson.Options).Trim('"');
        var count = answered.FirstOrDefault(row => row.Survey == surveyId && row.Key == question.Id)?.Count ?? 0;

        if (!published || count == 0)
        {
            return new KeyResult(question.Id, question.Text, type, auto, count, null, null, null, []);
        }

        if (question.Type == QuestionType.Nps)
        {
            var rows = values.Where(row => row.Survey == surveyId && row.Key == question.Id).ToList();
            int Sum(Func<double, bool> which) => rows.Where(row => which(row.Value)).Sum(row => row.Count);
            var (detractors, passives, promoters) = (Sum(value => value <= 6), Sum(value => value is > 6 and <= 8), Sum(value => value > 8));
            var all = Math.Max(1, detractors + passives + promoters);

            return new KeyResult(question.Id, question.Text, type, auto, count, Math.Round((promoters - detractors) * 100.0 / all), null, null,
                [new KeySlice("detractors", null, detractors), new KeySlice("passives", null, passives), new KeySlice("promoters", null, promoters)]);
        }

        if (Scales.Contains(question.Type))
        {
            var rows = values.Where(row => row.Survey == surveyId && row.Key == question.Id).OrderBy(row => row.Value).ToList();

            // Answered without a value (e.g. "don't know"): nothing to average yet.
            if (rows.Count == 0)
            {
                return new KeyResult(question.Id, question.Text, type, auto, count, null, question.Scale?.Max ?? 5, null, []);
            }

            var (min, max) = (question.Scale?.Min ?? 1, question.Scale?.Max ?? 5);
            var total = Math.Max(1, rows.Sum(row => row.Count));
            var average = rows.Sum(row => row.Value * row.Count) / total;
            var scale = max - min <= 10 ? Enumerable.Range(min, max - min + 1).Select(value => (double)value) : rows.Select(row => row.Value);

            return new KeyResult(question.Id, question.Text, type, auto, count, Math.Round(average, 1), max, null,
                [.. scale.Select(value => new KeySlice(value.ToString(System.Globalization.CultureInfo.InvariantCulture), null, rows.Where(row => row.Value == value).Sum(row => row.Count)))]);
        }

        // Choices: the share of those who answered that chose the top option, and every option's count, most first.
        var chosen = options.Where(row => row.Survey == surveyId && row.Key == question.Id).ToDictionary(row => row.Code, row => row.Count, StringComparer.Ordinal);
        var slices = AnswerText.Options(question).Select(option => new KeySlice(option.Code, option.Label, chosen.GetValueOrDefault(option.Code))).OrderByDescending(slice => slice.Count).ToList();
        var top = slices.FirstOrDefault();

        return new KeyResult(question.Id, question.Text, type, auto, count, top is null ? null : Math.Round(top.Count * 100.0 / count), null, top?.Label, slices);
    }

    private sealed class ValueRow
    {
        public Guid Survey { get; set; }

        public string Key { get; set; } = "";

        public double Value { get; set; }

        public int Count { get; set; }
    }

    private sealed class OptionRow
    {
        public Guid Survey { get; set; }

        public string Key { get; set; } = "";

        public string Code { get; set; } = "";

        public int Count { get; set; }
    }

    private sealed class AnsweredRow
    {
        public Guid Survey { get; set; }

        public string Key { get; set; } = "";

        public int Count { get; set; }
    }
}

/// <summary>A key question's result on a card (W38).</summary>
/// <param name="Question">The question's id.</param>
/// <param name="Text">Its text per language.</param>
/// <param name="Type">Its type as in the definition: nps, rating, slider, single, multiple, dropdown, yes-no.</param>
/// <param name="Auto">Picked because no question is flagged.</param>
/// <param name="Answered">How many answered it.</param>
/// <param name="Value">NPS: the score; rating and slider: the average; choices: the top option's share in percent. Null before answers.</param>
/// <param name="Max">Rating and slider: the top of the scale.</param>
/// <param name="Top">Choices: the top option's label.</param>
/// <param name="Spread">NPS: detractors, passives, promoters; scales: each value; choices: each option, most first.</param>
public sealed record KeyResult(string Question, TranslatedText Text, string Type, bool Auto, int Answered, double? Value, int? Max, TranslatedText? Top, IReadOnlyList<KeySlice> Spread);

/// <summary>One part of a key result's spread.</summary>
/// <param name="Code">The value, option code, or detractors / passives / promoters.</param>
/// <param name="Label">Choices: the option's label.</param>
/// <param name="Count">How many.</param>
public sealed record KeySlice(string Code, TranslatedText? Label, int Count);
