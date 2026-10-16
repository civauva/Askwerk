// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Exports;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Askwerk.Api.App.Results;

/// <summary>
/// Results of a survey (AWK-24, W11): an overview per question, the responses newest first on a cursor, and one
/// response. Every member of the organisation may read them; row-level security limits every query to it. The
/// overview aggregates in PostgreSQL, so it stays fast with many responses. Exports go to owners, admins, editors and
/// analysts, and into the audit log.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys/{surveyId:guid}")]
public sealed class ResultsController(AskwerkDbContext context) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private const int LatestTexts = 3;
    private static readonly DateTimeOffset Beginning = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The overview: totals, and a summary per question of the latest published version.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="filter">Which responses count.</param>
    /// <param name="language">The language for texts and labels; the survey's default when it does not offer it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The overview; 404 when the survey is not in this organisation.</returns>
    [HttpGet("results")]
    [ProducesResponseType<ResultsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResultsResponse>> Overview(Guid surveyId, [FromQuery] ResultsFilter filter, string? language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var survey = await context.Surveys.FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);

        if (survey is null)
        {
            return NotFound();
        }

        var versions = await PublishedVersionsAsync(surveyId, cancellationToken);
        var totals = (await QueryAsync<TotalsRow>(
            """
            SELECT count(*)::int AS "Responses",
                   (count(*) FILTER (WHERE r.status = 'complete'))::int AS "Complete",
                   (count(*) FILTER (WHERE r.status = 'partial'))::int AS "Partial",
                   (percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM r.completed_at - r.started_at)) FILTER (WHERE r.status = 'complete'))::float8 AS "MedianSeconds",
                   max(r.updated_at) AS "LastAt"
            """ + Responses,
            filter with { Status = null },
            cancellationToken)).Single();
        var shown = Shown(versions, filter.Version);

        if (shown is null)
        {
            return new ResultsResponse(survey.Title, survey.Languages, survey.Languages.FirstOrDefault() ?? "en", false, Totals(totals), [.. versions.Select(version => version.Number)], []);
        }

        var definition = shown.ReadDefinition();
        var chosen = definition.Languages.Contains(language ?? "") ? language! : definition.DefaultLanguage;
        var stats = (await QueryAsync<StatsRow>(
            """
            SELECT q.question_key AS "Key", count(*)::int AS "Answered",
                   (count(*) FILTER (WHERE a.detail ->> 'dontKnow' IS NOT NULL))::int AS "DontKnow",
                   avg(a.number_value)::float8 AS "Average",
                   (percentile_cont(0.5) WITHIN GROUP (ORDER BY a.number_value))::float8 AS "Median",
                   min(a.number_value)::float8 AS "Min", max(a.number_value)::float8 AS "Max",
                   min(a.date_value) AS "Earliest", max(a.date_value) AS "Latest"
            """ + Answers + " GROUP BY q.question_key",
            filter,
            cancellationToken)).ToDictionary(row => row.Key, StringComparer.Ordinal);
        var options = (await QueryAsync<OptionRow>(
            """
            SELECT q.question_key AS "Key", o.code AS "Code", count(*)::int AS "Count", avg(o.position)::float8 AS "Position"
            FROM answer a JOIN survey_question q ON q.id = a.question_id
                 CROSS JOIN LATERAL unnest(a.option_codes) WITH ORDINALITY AS o(code, position)
            """ + AnswersWhere + " GROUP BY q.question_key, o.code",
            filter,
            cancellationToken)).ToLookup(row => row.Key, StringComparer.Ordinal);
        var values = (await QueryAsync<ValueRow>(
            """
            SELECT q.question_key AS "Key", a.number_value::float8 AS "Value", count(*)::int AS "Count"
            """ + Answers + " AND q.type IN ('rating', 'nps', 'slider') AND a.number_value IS NOT NULL GROUP BY q.question_key, a.number_value",
            filter,
            cancellationToken)).ToLookup(row => row.Key, StringComparer.Ordinal);
        var cells = (await QueryAsync<CellRow>(
            """
            SELECT q.question_key AS "Key", m.key AS "Row", m.value AS "Code", count(*)::int AS "Count"
            FROM answer a JOIN survey_question q ON q.id = a.question_id
                 CROSS JOIN LATERAL jsonb_each_text(a.detail -> 'rows') AS m
            """ + AnswersWhere + " AND q.type = 'matrix' GROUP BY q.question_key, m.key, m.value",
            filter,
            cancellationToken)).ToLookup(row => row.Key, StringComparer.Ordinal);
        var texts = (await QueryAsync<TextRow>(
            """
            SELECT t."Key", t."Text", t."At" FROM (
                SELECT q.question_key AS "Key", a.text_value AS "Text", a.updated_at AS "At",
                       row_number() OVER (PARTITION BY q.question_key ORDER BY a.updated_at DESC, a.id) AS n
            """ + Answers + $" AND a.text_value IS NOT NULL) t WHERE t.n <= {LatestTexts} ORDER BY t.\"Key\", t.\"At\" DESC",
            filter,
            cancellationToken)).ToLookup(row => row.Key, StringComparer.Ordinal);

        string In(TranslatedText? text) => AnswerText.In(text, chosen, definition.DefaultLanguage);

        var questions = Questions(definition).Select(question =>
        {
            var stat = stats.GetValueOrDefault(question.Id);
            var counted = options[question.Id].ToDictionary(row => row.Code, StringComparer.Ordinal);
            var choices = AnswerText.Options(question)
                .Select(option => new OptionCount(option.Code, In(option.Label), counted.GetValueOrDefault(option.Code)?.Count ?? 0, question.Type == QuestionType.Ranking ? counted.GetValueOrDefault(option.Code)?.Position : null))
                .ToList();
            var distribution = Distribution(question, values[question.Id].ToList());

            return new QuestionResult(
                question.Id,
                question.Variable,
                TypeName(question.Type),
                In(question.Text),
                stat?.Answered ?? 0,
                stat?.DontKnow ?? 0,
                question.Type is QuestionType.Matrix || choices.Count == 0 ? null : choices,
                stat?.Average is { } average && question.Type is QuestionType.Rating or QuestionType.Nps or QuestionType.Slider or QuestionType.Number
                    ? new NumberSummary(average, stat.Median ?? average, stat.Min ?? average, stat.Max ?? average)
                    : null,
                distribution,
                question.Type == QuestionType.Nps && distribution is not null ? Nps(distribution) : null,
                question.Type == QuestionType.Matrix
                    ? [.. (question.Rows ?? []).Select(row => new MatrixRowResult(row.Code, In(row.Label), [.. (question.Options ?? []).Select(option =>
                        new OptionCount(option.Code, In(option.Label), cells[question.Id].Where(cell => cell.Row == row.Code && cell.Code == option.Code).Sum(cell => cell.Count), null))]))]
                    : null,
                question.Type is QuestionType.ShortText or QuestionType.LongText ? [.. texts[question.Id].Select(text => new TextAnswer(text.Text, text.At))] : null,
                question.Type == QuestionType.Date && stat?.Earliest is not null ? new DateSummary(stat.Earliest, stat.Latest ?? stat.Earliest) : null);
        });

        return new ResultsResponse(survey.Title, definition.Languages, definition.DefaultLanguage, true, Totals(totals), [.. versions.Select(version => version.Number)], [.. questions]);
    }

    /// <summary>Responses, newest or oldest first, a page at a time; the next page starts at <c>next</c>.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="filter">Which responses.</param>
    /// <param name="search">Part of a typed answer.</param>
    /// <param name="language">The language for labels.</param>
    /// <param name="cursor">Where the page starts; from the previous page's <c>next</c>.</param>
    /// <param name="limit">Responses per page (at most 200).</param>
    /// <param name="order">oldest for oldest first; newest first when empty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page; 404 when the survey is not in this organisation.</returns>
    [HttpGet("responses")]
    [ProducesResponseType<ResponseListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResponseListResponse>> List(
        Guid surveyId,
        [FromQuery] ResultsFilter filter,
        string? search,
        string? language,
        string? cursor,
        int? limit,
        string? order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!await context.Surveys.AnyAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken))
        {
            return NotFound();
        }

        var versions = await PublishedVersionsAsync(surveyId, cancellationToken);
        var shown = Shown(versions, filter.Version);
        var columns = shown is null ? [] : Questions(shown.ReadDefinition()).ToList();
        var pattern = string.IsNullOrWhiteSpace(search)
            ? ""
            : "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        const string Search = " AND (@search = '' OR EXISTS (SELECT 1 FROM answer s WHERE s.response_id = r.id AND (s.text_value ILIKE @search OR s.detail ->> 'other' ILIKE @search)))";
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var (after, afterId) = ReadCursor(cursor);
        var total = (await QueryAsync<CountRow>("SELECT count(*)::int AS \"Value\"" + Responses + Search, filter, cancellationToken, new NpgsqlParameter("search", pattern))).Single().Value;
        var ids = (await QueryAsync<IdRow>(
            "SELECT r.id AS \"Value\"" + Responses + Search
            + (order == "oldest"
                ? " AND (NOT @paged OR (r.started_at, r.id) > (@after, @afterId)) ORDER BY r.started_at, r.id LIMIT @take"
                : " AND (NOT @paged OR (r.started_at, r.id) < (@after, @afterId)) ORDER BY r.started_at DESC, r.id DESC LIMIT @take"),
            filter,
            cancellationToken,
            new NpgsqlParameter("search", pattern),
            new NpgsqlParameter("paged", after is not null),
            new NpgsqlParameter("after", NpgsqlDbType.TimestampTz) { Value = after ?? Beginning },
            new NpgsqlParameter("afterId", afterId),
            new NpgsqlParameter("take", size + 1))).Select(row => row.Value).ToList();
        var page = ids.Take(size).ToList();
        var responses = (await context.Responses.Where(response => page.Contains(response.Id)).ToListAsync(cancellationToken)).ToDictionary(response => response.Id);
        var answers = await AnswersOfAsync(page, cancellationToken);
        var definitions = versions.ToDictionary(version => version.Id, version => version.ReadDefinition());
        var fallback = shown?.ReadDefinition().DefaultLanguage ?? "en";
        var chosen = language ?? fallback;

        var items = page.Select(id =>
        {
            var response = responses[id];
            var questions = Questions(definitions[response.SurveyVersionId]).ToDictionary(question => question.Id, StringComparer.Ordinal);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var column in columns)
            {
                if (questions.TryGetValue(column.Id, out var question)
                    && answers[id].Where(answer => answer.Key == column.Id).Select(answer => answer.Answer).FirstOrDefault() is { } answer
                    && AnswerText.Format(question, answer.ToValue(question.Type), chosen, fallback) is { } text)
                {
                    texts[column.Variable] = text;
                }
            }

            return new ResponseRow(response.Id, Lower(response.Status), versions.Single(version => version.Id == response.SurveyVersionId).Number, response.Language, response.StartedAt, response.CompletedAt, Duration(response), texts);
        });
        var last = page.Count > 0 && ids.Count > size ? responses[page[^1]] : null;

        return new ResponseListResponse(
            total,
            [.. columns.Select(question => new ResponseColumn(question.Id, question.Variable, TypeName(question.Type), AnswerText.In(question.Text, chosen, fallback)))],
            [.. items],
            last is null ? null : WriteCursor(last.StartedAt, last.Id));
    }

    /// <summary>One response with every question of its version, in the language it was answered in.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="responseId">The response.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response; 404 when it is not one of this survey's.</returns>
    [HttpGet("responses/{responseId:guid}")]
    [ProducesResponseType<ResponseDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResponseDetail>> Get(Guid surveyId, Guid responseId, CancellationToken cancellationToken)
    {
        var response = await context.Responses.FirstOrDefaultAsync(row => row.Id == responseId && row.SurveyId == surveyId, cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        var version = await context.SurveyVersions.SingleAsync(row => row.Id == response.SurveyVersionId, cancellationToken);
        var definition = version.ReadDefinition();
        var answers = (await AnswersOfAsync([responseId], cancellationToken))[responseId].ToDictionary(answer => answer.Key, answer => answer.Answer, StringComparer.Ordinal);
        var language = response.Language;
        var fallback = definition.DefaultLanguage;
        var items = new List<AnsweredQuestion>();

        foreach (var question in Questions(definition))
        {
            var text = AnswerText.In(question.Text, language, fallback);
            var value = answers.GetValueOrDefault(question.Id)?.ToValue(question.Type);

            if (question.Type == QuestionType.Matrix && value?.Rows is { Count: > 0 } rows && value.DontKnow != true)
            {
                items.AddRange(AnswerText.Rows(question, rows, language, fallback).Select(row => new AnsweredQuestion(question.Id, question.Variable, $"{text} · {row.Row}", row.Value)));
                continue;
            }

            items.Add(new AnsweredQuestion(question.Id, question.Variable, text, value is null ? null : AnswerText.Format(question, value, language, fallback)));
        }

        return new ResponseDetail(response.Id, Lower(response.Status), version.Number, language, response.StartedAt, response.CompletedAt, Duration(response), items);
    }

    // The responses a filter selects; the survey, status, time and version are parameters.
    private const string Responses =
        """

        FROM response r JOIN survey_version v ON v.id = r.survey_version_id
        WHERE r.survey_id = @survey AND (@status = 'all' OR r.status = @status)
          AND r.started_at >= @from AND r.started_at < @to AND (@version = 0 OR v.number = @version)
        """;

    // The answers of the responses a filter selects, with their question.
    private const string Answers =
        """

        FROM answer a JOIN survey_question q ON q.id = a.question_id
        """ + AnswersWhere;

    private const string AnswersWhere =
        """

        JOIN response r ON r.id = a.response_id JOIN survey_version v ON v.id = r.survey_version_id
        WHERE r.survey_id = @survey AND (@status = 'all' OR r.status = @status)
          AND r.started_at >= @from AND r.started_at < @to AND (@version = 0 OR v.number = @version)
        """;

    private ObjectResult Refused(int status, string code) =>
        StatusCode(status, new ResultsProblem(code, System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    private static IEnumerable<Question> Questions(SurveyDefinition definition) =>
        definition.Pages.SelectMany(page => page.Questions).Where(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image));

    private static SurveyVersion? Shown(List<SurveyVersion> versions, int? number) =>
        number is > 0 ? versions.FirstOrDefault(version => version.Number == number) : versions.LastOrDefault();

    private static ResultTotals Totals(TotalsRow row) => new(row.Responses, row.Complete, row.Partial, row.MedianSeconds, row.LastAt);

    private static int? Duration(SurveyResponse response) => response.CompletedAt is { } completed ? (int)Math.Round((completed - response.StartedAt).TotalSeconds) : null;

    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private static string TypeName(QuestionType type) =>
        System.Text.Json.JsonSerializer.Serialize(type, SurveyDefinitionJson.Options).Trim('"');

    /// <summary>Counts per value, with every value of the scale, also unchosen ones, while there are few.</summary>
    private static List<ValueCount>? Distribution(Question question, List<ValueRow> rows)
    {
        if (question.Type is not (QuestionType.Rating or QuestionType.Nps or QuestionType.Slider))
        {
            return null;
        }

        var (min, max, step) = question.Type == QuestionType.Nps
            ? (0, 10, 1)
            : (question.Scale?.Min ?? 1, question.Scale?.Max ?? 5, Math.Max(1, question.Scale?.Step ?? 1));
        var counts = rows.ToDictionary(row => row.Value, row => row.Count);
        var scale = (max - min) / step <= 20 ? Enumerable.Range(0, ((max - min) / step) + 1).Select(index => (double)(min + (index * step))) : [];

        return [.. scale.Union(counts.Keys).Order().Select(value => new ValueCount(value, counts.GetValueOrDefault(value)))];
    }

    private static NpsSummary Nps(List<ValueCount> distribution)
    {
        var detractors = distribution.Where(value => value.Value <= 6).Sum(value => value.Count);
        var passives = distribution.Where(value => value.Value is > 6 and <= 8).Sum(value => value.Count);
        var promoters = distribution.Where(value => value.Value > 8).Sum(value => value.Count);
        var all = detractors + passives + promoters;

        return new NpsSummary(all == 0 ? 0 : (int)Math.Round((promoters - detractors) * 100.0 / all), detractors, passives, promoters);
    }

    private static string WriteCursor(DateTimeOffset startedAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{startedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (DateTimeOffset? At, Guid Id) ReadCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (null, Guid.Empty);
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='))).Split(':');

            return (new DateTimeOffset(long.Parse(parts[0], CultureInfo.InvariantCulture), TimeSpan.Zero), Guid.ParseExact(parts[1], "N"));
        }
        catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return (null, Guid.Empty);
        }
    }

    private async Task<List<SurveyVersion>> PublishedVersionsAsync(Guid surveyId, CancellationToken cancellationToken) =>
        await context.SurveyVersions
            .Where(version => version.SurveyId == surveyId && version.State != SurveyVersionState.Draft)
            .OrderBy(version => version.Number)
            .ToListAsync(cancellationToken);

    private async Task<ILookup<Guid, (string Key, Answer Answer)>> AnswersOfAsync(List<Guid> responseIds, CancellationToken cancellationToken)
    {
        var rows = await context.Answers
            .Where(answer => responseIds.Contains(answer.ResponseId))
            .Join(context.SurveyQuestions, answer => answer.QuestionId, question => question.Id, (answer, question) => new { answer, question.QuestionKey })
            .ToListAsync(cancellationToken);

        return rows.ToLookup(row => row.answer.ResponseId, row => (row.QuestionKey, row.answer));
    }

    private Task<List<T>> QueryAsync<T>(string sql, ResultsFilter filter, CancellationToken cancellationToken, params NpgsqlParameter[] extra)
    {
        var surveyId = Guid.Parse(Convert.ToString(RouteData.Values["surveyId"], CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        NpgsqlParameter[] parameters =
        [
            new("survey", surveyId),
            new("status", filter.Status is "complete" or "partial" ? filter.Status : "all"),
            new("from", NpgsqlDbType.TimestampTz) { Value = filter.From?.ToUniversalTime() ?? Beginning },
            new("to", NpgsqlDbType.TimestampTz) { Value = filter.To?.ToUniversalTime() ?? End },
            new("version", filter.Version ?? 0),
            .. extra,
        ];

        return context.Database.SqlQueryRaw<T>(sql, parameters).ToListAsync(cancellationToken);
    }

    private sealed class TotalsRow
    {
        public int Responses { get; set; }

        public int Complete { get; set; }

        public int Partial { get; set; }

        public double? MedianSeconds { get; set; }

        public DateTimeOffset? LastAt { get; set; }
    }

    private sealed class StatsRow
    {
        public string Key { get; set; } = "";

        public int Answered { get; set; }

        public int DontKnow { get; set; }

        public double? Average { get; set; }

        public double? Median { get; set; }

        public double? Min { get; set; }

        public double? Max { get; set; }

        public string? Earliest { get; set; }

        public string? Latest { get; set; }
    }

    private sealed class OptionRow
    {
        public string Key { get; set; } = "";

        public string Code { get; set; } = "";

        public int Count { get; set; }

        public double? Position { get; set; }
    }

    private sealed class ValueRow
    {
        public string Key { get; set; } = "";

        public double Value { get; set; }

        public int Count { get; set; }
    }

    private sealed class CellRow
    {
        public string Key { get; set; } = "";

        public string Row { get; set; } = "";

        public string Code { get; set; } = "";

        public int Count { get; set; }
    }

    private sealed class TextRow
    {
        public string Key { get; set; } = "";

        public string Text { get; set; } = "";

        public DateTimeOffset At { get; set; }
    }

    private sealed class CountRow
    {
        public int Value { get; set; }
    }

    private sealed class IdRow
    {
        public Guid Value { get; set; }
    }
}

/// <summary>An export (W11 C).</summary>
/// <param name="Format">excel, csv, spss, stata or json.</param>
/// <param name="Layout">wide (default) or long; CSV and Excel only.</param>
/// <param name="Values">labels (default), codes or both; CSV and Excel only.</param>
/// <param name="Status">complete or partial; all when empty.</param>
/// <param name="From">Started at or after (UTC).</param>
/// <param name="To">Started before (UTC).</param>
/// <param name="Version">One published version; all when empty. Its codebook is used; the latest otherwise.</param>
/// <param name="Language">The language of labels; the survey's default when it does not offer it.</param>
public sealed record ExportRequest(string Format, string? Layout = null, string? Values = null, string? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int? Version = null, string? Language = null);

/// <summary>Why the results refused a request.</summary>
/// <param name="Code">role-not-allowed, not-published or too-many-rows-for-excel.</param>
/// <param name="TraceId">The request's trace, to quote to support.</param>
public sealed record ResultsProblem(string Code, string? TraceId);

/// <summary>Which responses results count (W11, the filter chips).</summary>
/// <param name="Status">complete or partial; all when empty.</param>
/// <param name="From">Started at or after (UTC).</param>
/// <param name="To">Started before (UTC).</param>
/// <param name="Version">One published version; all when empty.</param>
public sealed record ResultsFilter(string? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int? Version = null);

/// <summary>The overview of a survey's results.</summary>
/// <param name="Title">The survey's internal name.</param>
/// <param name="Languages">Languages of the version shown.</param>
/// <param name="DefaultLanguage">Its default language.</param>
/// <param name="Published">Whether the survey was ever published; there are no results before.</param>
/// <param name="Totals">Counts over all statuses.</param>
/// <param name="Versions">Published version numbers, for the version filter.</param>
/// <param name="Questions">A summary per question, in the survey's order.</param>
public sealed record ResultsResponse(string Title, IReadOnlyList<string> Languages, string DefaultLanguage, bool Published, ResultTotals Totals, IReadOnlyList<int> Versions, IReadOnlyList<QuestionResult> Questions);

/// <summary>Counts over the filtered time and version, all statuses.</summary>
/// <param name="Responses">All responses.</param>
/// <param name="Complete">Finished ones.</param>
/// <param name="Partial">Started, not finished.</param>
/// <param name="MedianSeconds">Median time to finish.</param>
/// <param name="LastAt">The last change.</param>
public sealed record ResultTotals(int Responses, int Complete, int Partial, double? MedianSeconds, DateTimeOffset? LastAt);

/// <summary>The summary of one question; only the parts its type has are set.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Variable">Its variable name.</param>
/// <param name="Type">Its type, as in the definition.</param>
/// <param name="Text">Its text in the language.</param>
/// <param name="Answered">Responses with an answer.</param>
/// <param name="DontKnow">Of those, "Don't know".</param>
/// <param name="Options">Choices: count per option, in the survey's order.</param>
/// <param name="Numbers">Ratings, sliders, numbers: average, median, smallest and largest.</param>
/// <param name="Distribution">Ratings, NPS, sliders: count per value.</param>
/// <param name="Nps">NPS: the score and its three groups.</param>
/// <param name="Rows">Matrix: counts per row.</param>
/// <param name="Latest">Texts: the latest answers.</param>
/// <param name="Dates">Dates: earliest and latest.</param>
public sealed record QuestionResult(
    string Id,
    string Variable,
    string Type,
    string Text,
    int Answered,
    int DontKnow,
    IReadOnlyList<OptionCount>? Options,
    NumberSummary? Numbers,
    IReadOnlyList<ValueCount>? Distribution,
    NpsSummary? Nps,
    IReadOnlyList<MatrixRowResult>? Rows,
    IReadOnlyList<TextAnswer>? Latest,
    DateSummary? Dates);

/// <summary>How often an option was chosen.</summary>
/// <param name="Code">The option's code.</param>
/// <param name="Label">Its label.</param>
/// <param name="Count">Times chosen.</param>
/// <param name="AveragePosition">Ranking: its average place, 1 = first.</param>
public sealed record OptionCount(string Code, string Label, int Count, double? AveragePosition);

/// <summary>Numbers of a numeric question.</summary>
/// <param name="Average">Mean.</param>
/// <param name="Median">Median.</param>
/// <param name="Min">Smallest.</param>
/// <param name="Max">Largest.</param>
public sealed record NumberSummary(double Average, double Median, double Min, double Max);

/// <summary>How often a value was given.</summary>
/// <param name="Value">The value.</param>
/// <param name="Count">Times given.</param>
public sealed record ValueCount(double Value, int Count);

/// <summary>Net Promoter Score.</summary>
/// <param name="Score">Promoters minus detractors, in percent of all.</param>
/// <param name="Detractors">0–6.</param>
/// <param name="Passives">7–8.</param>
/// <param name="Promoters">9–10.</param>
public sealed record NpsSummary(int Score, int Detractors, int Passives, int Promoters);

/// <summary>One row of a matrix question.</summary>
/// <param name="Code">The row's code.</param>
/// <param name="Label">Its label.</param>
/// <param name="Options">Count per option in this row.</param>
public sealed record MatrixRowResult(string Code, string Label, IReadOnlyList<OptionCount> Options);

/// <summary>A typed answer.</summary>
/// <param name="Text">The text.</param>
/// <param name="At">When it was given.</param>
public sealed record TextAnswer(string Text, DateTimeOffset At);

/// <summary>The range of a date question's answers.</summary>
/// <param name="Earliest">Earliest, as written.</param>
/// <param name="Latest">Latest, as written.</param>
public sealed record DateSummary(string Earliest, string Latest);

/// <summary>One page of responses.</summary>
/// <param name="Total">Responses the filter selects.</param>
/// <param name="Columns">The questions, in the survey's order.</param>
/// <param name="Items">The responses, newest first.</param>
/// <param name="Next">The cursor of the next page; null on the last.</param>
public sealed record ResponseListResponse(int Total, IReadOnlyList<ResponseColumn> Columns, IReadOnlyList<ResponseRow> Items, string? Next);

/// <summary>A question as a column of the response list.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Variable">Its variable name, the column's key.</param>
/// <param name="Type">Its type.</param>
/// <param name="Text">Its text in the language.</param>
public sealed record ResponseColumn(string Id, string Variable, string Type, string Text);

/// <summary>A response in the list.</summary>
/// <param name="Id">The response.</param>
/// <param name="Status">complete or partial.</param>
/// <param name="Version">The version answered.</param>
/// <param name="Language">The language answered in.</param>
/// <param name="StartedAt">Started (UTC).</param>
/// <param name="CompletedAt">Finished (UTC).</param>
/// <param name="DurationSeconds">Time to finish.</param>
/// <param name="Answers">Answers as text, by the column's variable name.</param>
public sealed record ResponseRow(Guid Id, string Status, int Version, string Language, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int? DurationSeconds, IReadOnlyDictionary<string, string> Answers);

/// <summary>One response with all its answers.</summary>
/// <param name="Id">The response.</param>
/// <param name="Status">complete or partial.</param>
/// <param name="Version">The version answered.</param>
/// <param name="Language">The language answered in; texts are in it.</param>
/// <param name="StartedAt">Started (UTC).</param>
/// <param name="CompletedAt">Finished (UTC).</param>
/// <param name="DurationSeconds">Time to finish.</param>
/// <param name="Answers">Every question of the version, in order; a matrix gives one line per answered row.</param>
public sealed record ResponseDetail(Guid Id, string Status, int Version, string Language, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int? DurationSeconds, IReadOnlyList<AnsweredQuestion> Answers);

/// <summary>A question and its answer.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Variable">Its variable name.</param>
/// <param name="Text">Its text.</param>
/// <param name="Value">The answer as text; null when not answered.</param>
public sealed record AnsweredQuestion(string Id, string Variable, string Text, string? Value);
