// SPDX-License-Identifier: Apache-2.0
// Copy and adapt freely. Pulls Askwerk responses into SQL Server tables in long format (create-tables.sql).
using System.Data;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AskwerkSqlSync;

/// <summary>
/// Copies one survey after another: refreshes its codebook, then pages through the responses changed since the last
/// stored sequence. Each page is one transaction (responses, their answers and the new sequence together), so a crash
/// repeats at most one page and a second run changes nothing.
/// </summary>
/// <param name="api">An HttpClient with the Askwerk address as BaseAddress and the API key as bearer token.</param>
/// <param name="connectionString">The SQL Server database holding the tables of create-tables.sql.</param>
/// <param name="language">The language of the labels, e.g. "de"; the survey's default language when null.</param>
public sealed class Sync(HttpClient api, string connectionString, string? language)
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Every survey with a published version.</summary>
    public async Task<IReadOnlyList<Guid>> PublishedSurveysAsync(CancellationToken cancellationToken)
    {
        var surveys = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await GetAsync<SurveyPage>($"api/v1/surveys?limit=100{(cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))}", cancellationToken);
            surveys.AddRange(page.Items.Where(survey => survey.PublishedVersion is not null).Select(survey => survey.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return surveys;
    }

    /// <summary>Copies one survey; returns how many responses were new or changed. Stores an error in askwerk_sync_state and rethrows it.</summary>
    public async Task<int> RunAsync(Guid surveyId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            var codebook = await GetAsync<Codebook>($"api/v1/surveys/{surveyId}/codebook{Query()}", cancellationToken);
            await StoreCodebookAsync(connection, surveyId, codebook, cancellationToken);
            var numeric = codebook.Variables.Where(variable => variable.Numeric).Select(variable => variable.Variable).ToHashSet(StringComparer.Ordinal);

            var since = await LastSequenceAsync(connection, surveyId, cancellationToken);
            var copied = 0;

            while (true)
            {
                var page = await GetAsync<ResponsePage>($"api/v1/surveys/{surveyId}/responses?since={since}&limit={PageSize}{Query("&")}", cancellationToken);

                if (page.Items.Count == 0)
                {
                    break;
                }

                await StorePageAsync(connection, surveyId, page, numeric, cancellationToken);
                copied += page.Items.Count;
                since = page.NextSince;
            }

            await SetStateAsync(connection, surveyId, since, null, cancellationToken);

            return copied;
        }
        catch (Exception error) when (error is HttpRequestException or SqlException or JsonException)
        {
            await SetStateAsync(connection, surveyId, null, error.Message, cancellationToken);
            throw;
        }
    }

    private string Query(string separator = "?") => language is null ? "" : $"{separator}language={Uri.EscapeDataString(language)}";

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await api.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"GET {path}: {(int)response.StatusCode} {problem}", null, response.StatusCode);
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private static async Task<long> LastSequenceAsync(SqlConnection connection, Guid surveyId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT last_sequence FROM askwerk_sync_state WHERE survey_id = @survey", connection);
        command.Parameters.AddWithValue("@survey", surveyId);

        return await command.ExecuteScalarAsync(cancellationToken) is long last ? last : 0;
    }

    private static async Task SetStateAsync(SqlConnection connection, Guid surveyId, long? sequence, string? error, CancellationToken cancellationToken, SqlTransaction? transaction = null)
    {
        const string Sql = """
            MERGE askwerk_sync_state AS target
            USING (SELECT @survey AS survey_id) AS source ON target.survey_id = source.survey_id
            WHEN MATCHED THEN UPDATE SET last_sequence = COALESCE(@sequence, target.last_sequence), last_run_at = SYSDATETIMEOFFSET(), last_error = @error
            WHEN NOT MATCHED THEN INSERT (survey_id, last_sequence, last_run_at, last_error) VALUES (@survey, COALESCE(@sequence, 0), SYSDATETIMEOFFSET(), @error);
            """;
        await using var command = new SqlCommand(Sql, connection, transaction);
        command.Parameters.AddWithValue("@survey", surveyId);
        command.Parameters.AddWithValue("@sequence", (object?)sequence ?? DBNull.Value);
        command.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task StoreCodebookAsync(SqlConnection connection, Guid surveyId, Codebook codebook, CancellationToken cancellationToken)
    {
        const string Variable = """
            MERGE askwerk_variable AS target
            USING (SELECT @survey AS survey_id, @version AS version, @variable AS variable) AS source
              ON target.survey_id = source.survey_id AND target.version = source.version AND target.variable = source.variable
            WHEN MATCHED THEN UPDATE SET label = @label, is_numeric = @numeric, language = @language
            WHEN NOT MATCHED THEN INSERT (survey_id, version, variable, label, is_numeric, language) VALUES (@survey, @version, @variable, @label, @numeric, @language);
            """;
        const string Value = """
            MERGE askwerk_value_label AS target
            USING (SELECT @survey AS survey_id, @variable AS variable, @code AS code, @language AS language) AS source
              ON target.survey_id = source.survey_id AND target.variable = source.variable AND target.code = source.code AND target.language = source.language
            WHEN MATCHED THEN UPDATE SET label = @label
            WHEN NOT MATCHED THEN INSERT (survey_id, variable, code, language, label) VALUES (@survey, @variable, @code, @language, @label);
            """;

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var variable in codebook.Variables)
        {
            await using (var command = new SqlCommand(Variable, connection, transaction))
            {
                command.Parameters.AddWithValue("@survey", surveyId);
                command.Parameters.AddWithValue("@version", codebook.Version);
                command.Parameters.AddWithValue("@variable", variable.Variable);
                command.Parameters.AddWithValue("@label", variable.Label);
                command.Parameters.AddWithValue("@numeric", variable.Numeric);
                command.Parameters.AddWithValue("@language", codebook.Language);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var value in variable.Values)
            {
                await using var command = new SqlCommand(Value, connection, transaction);
                command.Parameters.AddWithValue("@survey", surveyId);
                command.Parameters.AddWithValue("@variable", variable.Variable);
                command.Parameters.AddWithValue("@code", value.Code);
                command.Parameters.AddWithValue("@language", codebook.Language);
                command.Parameters.AddWithValue("@label", value.Label);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task StorePageAsync(SqlConnection connection, Guid surveyId, ResponsePage page, HashSet<string> numeric, CancellationToken cancellationToken)
    {
        const string Response = """
            MERGE askwerk_response AS target
            USING (SELECT @id AS response_id) AS source ON target.response_id = source.response_id
            WHEN MATCHED THEN UPDATE SET version = @version, status = @status, language = @language, participant_email = @email,
              external_id = @external, started_at = @started, completed_at = @completed, change_sequence = @sequence
            WHEN NOT MATCHED THEN INSERT (response_id, survey_id, version, status, language, participant_email, external_id, started_at, completed_at, change_sequence)
              VALUES (@id, @survey, @version, @status, @language, @email, @external, @started, @completed, @sequence);
            DELETE FROM askwerk_answer WHERE response_id = @id;
            """;

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        using var answers = new DataTable();
        answers.Columns.Add("response_id", typeof(Guid));
        answers.Columns.Add("variable", typeof(string));
        answers.Columns.Add("value", typeof(string));
        answers.Columns.Add("value_number", typeof(decimal));
        answers.Columns.Add("label", typeof(string));

        foreach (var response in page.Items)
        {
            await using var command = new SqlCommand(Response, connection, transaction);
            command.Parameters.AddWithValue("@id", response.Id);
            command.Parameters.AddWithValue("@survey", surveyId);
            command.Parameters.AddWithValue("@version", response.Version);
            command.Parameters.AddWithValue("@status", response.Status);
            command.Parameters.AddWithValue("@language", response.Language);
            command.Parameters.AddWithValue("@email", (object?)response.Participant?.Email ?? DBNull.Value);
            command.Parameters.AddWithValue("@external", (object?)response.Participant?.ExternalId ?? DBNull.Value);
            command.Parameters.AddWithValue("@started", response.StartedAt);
            command.Parameters.AddWithValue("@completed", (object?)response.CompletedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@sequence", response.ChangeSequence);
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (var answer in response.Answers)
            {
                object number = numeric.Contains(answer.Variable) && decimal.TryParse(answer.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : DBNull.Value;
                answers.Rows.Add(response.Id, answer.Variable, answer.Value, number, (object?)answer.Label ?? DBNull.Value);
            }
        }

        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "askwerk_answer" })
        {
            foreach (DataColumn column in answers.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulk.WriteToServerAsync(answers, cancellationToken);
        }

        await SetStateAsync(connection, surveyId, page.NextSince, null, cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record SurveyPage(IReadOnlyList<SurveyItem> Items, string? NextCursor);

    private sealed record SurveyItem(Guid Id, int? PublishedVersion);

    private sealed record Codebook(int Version, string Language, IReadOnlyList<CodebookVariable> Variables);

    private sealed record CodebookVariable(string Variable, string Label, bool Numeric, IReadOnlyList<CodebookValue> Values);

    private sealed record CodebookValue(string Code, string Label);

    private sealed record ResponsePage(IReadOnlyList<Response> Items, long NextSince);

    private sealed record Response(Guid Id, string Status, int Version, string Language, Participant? Participant, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long ChangeSequence, IReadOnlyList<Answer> Answers);

    private sealed record Participant(string? Email, string? ExternalId);

    private sealed record Answer(string Variable, string Value, string? Label);
}
