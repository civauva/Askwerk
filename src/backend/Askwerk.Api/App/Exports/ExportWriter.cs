// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Results;
using Askwerk.Application.Exports;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Askwerk.Api.App.Exports;

/// <summary>
/// Writes a survey's results to a file in a chosen format (AWK-24, AWK-25): the questions of the shown version,
/// the codebook in the chosen language, and the responses the filter selects, oldest first, read in batches of 500
/// while the file is written.
/// </summary>
public sealed class ExportWriter(AskwerkDbContext context, IEnumerable<IExporter> exporters)
{
    private const int Batch = 500;
    private static readonly DateTimeOffset Beginning = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // The responses a filter selects; the survey, status, time and version are parameters.
    private const string Responses =
        """

        FROM response r JOIN survey_version v ON v.id = r.survey_version_id
        WHERE r.survey_id = @survey AND (@status = 'all' OR r.status = @status)
          AND r.started_at >= @from AND r.started_at < @to AND (@version = 0 OR v.number = @version)
        """;

    /// <summary>The exporter of a format's name, or null for an unknown one.</summary>
    /// <param name="format">excel, csv, spss, stata or json.</param>
    /// <returns>The exporter.</returns>
    public IExporter? ExporterOf(string? format) =>
        Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(format, out _)
            ? exporters.FirstOrDefault(each => each.Format == parsed)
            : null;

    /// <summary>The published versions of a survey, oldest first.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The versions.</returns>
    public Task<List<SurveyVersion>> PublishedVersionsAsync(Guid surveyId, CancellationToken cancellationToken) =>
        context.SurveyVersions
            .Where(version => version.SurveyId == surveyId && version.State != SurveyVersionState.Draft)
            .OrderBy(version => version.Number)
            .ToListAsync(cancellationToken);

    /// <summary>The version an export shows: the chosen one, else the latest published.</summary>
    /// <param name="versions">The published versions.</param>
    /// <param name="number">The chosen version, if any.</param>
    /// <returns>The version, or null before publishing.</returns>
    public static SurveyVersion? Shown(List<SurveyVersion> versions, int? number) =>
        number is > 0 ? versions.FirstOrDefault(version => version.Number == number) : versions.LastOrDefault();

    /// <summary>How many responses the filter selects.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count.</returns>
    public async Task<int> CountAsync(Guid surveyId, ResultsFilter filter, CancellationToken cancellationToken) =>
        (await QueryAsync<CountRow>("SELECT count(*)::int AS \"Value\"" + Responses, surveyId, filter, cancellationToken)).Single().Value;

    /// <summary>Writes the export.</summary>
    /// <param name="survey">The survey.</param>
    /// <param name="exporter">The format's exporter.</param>
    /// <param name="request">Layout, values, language and filter.</param>
    /// <param name="exportedAt">Now.</param>
    /// <param name="target">Where the file goes.</param>
    /// <param name="progress">Called after every batch with the responses written so far.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The file's name.</returns>
    /// <exception cref="ExportTooLargeException">The format cannot hold the export.</exception>
    public async Task<string> WriteAsync(Survey survey, IExporter exporter, ExportRequest request, DateTimeOffset exportedAt, Stream target, Func<int, Task> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(request);
        var versions = await PublishedVersionsAsync(survey.Id, cancellationToken);
        var definition = (Shown(versions, request.Version) ?? throw new InvalidOperationException("The survey is not published.")).ReadDefinition();
        var language = definition.Languages.Contains(request.Language ?? "") ? request.Language! : definition.DefaultLanguage;
        var versionIds = versions.Select(version => version.Id).ToList();
        var questions = (await context.SurveyQuestions
                .Where(question => versionIds.Contains(question.SurveyVersionId))
                .Select(question => new { question.Id, question.QuestionKey, question.Type })
                .ToListAsync(cancellationToken))
            .ToDictionary(question => question.Id, question => (question.QuestionKey, Type: JsonSerializer.Deserialize<QuestionType>($"\"{question.Type}\"", SurveyDefinitionJson.Options)));
        var options = new ExportOptions(
            Enum.TryParse<ExportLayout>(request.Layout, ignoreCase: true, out var layout) ? layout : ExportLayout.Wide,
            Enum.TryParse<ExportValues>(request.Values, ignoreCase: true, out var values) ? values : ExportValues.Labels);
        var filter = new ResultsFilter(request.Status, request.From, request.To, request.Version);
        var fileName = Slug(survey.Title);
        var data = new ExportData(survey.Id, survey.Title, fileName, Codebook.Of(definition, language), exportedAt, ReadAsync(survey.Id, filter, versions, questions, progress, cancellationToken));

        await exporter.WriteAsync(data, options, target, cancellationToken);

        var suffix = exporter.Format switch
        {
            ExportFormat.Spss => "-spss",
            ExportFormat.Stata => "-stata",
            _ => "",
        };

        return fileName + suffix + exporter.Extension;
    }

    /// <summary>A file name from a title: lower-case letters and digits, joined by hyphens.</summary>
    private static string Slug(string title)
    {
        var slug = new StringBuilder();

        foreach (var character in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var name = slug.ToString().Trim('-');

        return name.Length == 0 ? "results" : name[..Math.Min(name.Length, 80)].TrimEnd('-');
    }

    /// <summary>The filtered responses, oldest first, a batch at a time, with their answers.</summary>
    private async IAsyncEnumerable<ExportResponse> ReadAsync(
        Guid surveyId,
        ResultsFilter filter,
        List<SurveyVersion> versions,
        Dictionary<Guid, (string QuestionKey, QuestionType Type)> questions,
        Func<int, Task> progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var numbers = versions.ToDictionary(version => version.Id, version => version.Number);
        DateTimeOffset? after = null;
        var afterId = Guid.Empty;
        var written = 0;

        while (true)
        {
            var ids = (await QueryAsync<IdRow>(
                "SELECT r.id AS \"Value\"" + Responses + " AND (NOT @paged OR (r.started_at, r.id) > (@after, @afterId)) ORDER BY r.started_at, r.id LIMIT @take",
                surveyId,
                filter,
                cancellationToken,
                new NpgsqlParameter("paged", after is not null),
                new NpgsqlParameter("after", NpgsqlDbType.TimestampTz) { Value = after ?? Beginning },
                new NpgsqlParameter("afterId", afterId),
                new NpgsqlParameter("take", Batch))).Select(row => row.Value).ToList();

            if (ids.Count == 0)
            {
                yield break;
            }

            var responses = await context.Responses.AsNoTracking().Where(response => ids.Contains(response.Id)).ToDictionaryAsync(response => response.Id, cancellationToken);
            var answers = (await context.Answers.AsNoTracking().Where(answer => ids.Contains(answer.ResponseId)).ToListAsync(cancellationToken)).ToLookup(answer => answer.ResponseId);

            // In the database's order: PostgreSQL and .NET sort ids differently.
            foreach (var id in ids)
            {
                var response = responses[id];
                var values = new Dictionary<string, AnswerValue>(StringComparer.Ordinal);

                foreach (var answer in answers[id])
                {
                    if (questions.TryGetValue(answer.QuestionId, out var question))
                    {
                        values[question.QuestionKey] = answer.ToValue(question.Type);
                    }
                }

                yield return new ExportResponse(response.Id, response.Status.ToString().ToLowerInvariant(), numbers[response.SurveyVersionId], response.Language, response.StartedAt, response.CompletedAt, values);
            }

            written += ids.Count;
            await progress(written);
            var last = responses[ids[^1]];
            (after, afterId) = (last.StartedAt, last.Id);
        }
    }

    private Task<List<T>> QueryAsync<T>(string sql, Guid surveyId, ResultsFilter filter, CancellationToken cancellationToken, params NpgsqlParameter[] extra)
    {
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

    private sealed class CountRow
    {
        public int Value { get; set; }
    }

    private sealed class IdRow
    {
        public Guid Value { get; set; }
    }
}
