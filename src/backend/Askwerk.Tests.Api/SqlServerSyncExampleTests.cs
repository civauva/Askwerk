// SPDX-License-Identifier: AGPL-3.0-only
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Askwerk.Tests.Integration;
using AskwerkSqlSync;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;

namespace Askwerk.Tests.Api;

/// <summary>
/// The SQL Server sync example (docs/integrations/sql-server-sync, AWK-14) against this API and SQL Server 2022 in a
/// container. Slow to start, so it runs in the full run only (ASKWERK_TEST_SQLSERVER=1, set by bin/test-all.sh --full).
/// </summary>
public sealed class SqlServerSyncExampleTests(PostgresServer server)
{
    private const string Password = "Askwerk-Test-1";
    private static readonly string[] Ads = ["2"];

    public static bool Enabled => Environment.GetEnvironmentVariable("ASKWERK_TEST_SQLSERVER") == "1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact(SkipUnless = nameof(Enabled), Skip = "SQL Server runs in the full run only (ASKWERK_TEST_SQLSERVER=1).")]
    public async Task TheJob_CopiesResponsesInLongFormat_RepeatsNothing_AndPicksUpChanges()
    {
        await using var sql = new ContainerBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithEnvironment("MSSQL_SA_PASSWORD", Password)
            .WithPortBinding(1433, true)
            .Build();
        var started = sql.StartAsync(Cancel);

        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var respondent = await factory.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 4 }, q2 = new { options = Ads } } }, Cancel)).EnsureSuccessStatusCode();
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner);
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        await started;
        var connectionString = await ReadyDatabaseAsync($"Server={sql.Hostname},{sql.GetMappedPublicPort(1433)};User Id=sa;Password={Password};TrustServerCertificate=true");
        var sync = new Sync(api, connectionString, "de");
        var survey = Guid.Parse(id);

        Assert.Equal([survey], await sync.PublishedSurveysAsync(Cancel));
        Assert.Equal(1, await sync.RunAsync(survey, Cancel));
        Assert.Equal(0, await sync.RunAsync(survey, Cancel));
        Assert.Equal("4|4", await ScalarAsync(connectionString, "SELECT value + '|' + CAST(CAST(value_number AS int) AS nvarchar) FROM askwerk_answer WHERE variable = 'SAT'"));
        Assert.Equal("1", await ScalarAsync(connectionString, "SELECT value FROM askwerk_answer WHERE variable = 'SOURCE_2'"));
        Assert.Equal("Ja", await ScalarAsync(connectionString, "SELECT label FROM askwerk_value_label WHERE variable = 'BEFORE' AND code = '1' AND language = 'de'"));
        Assert.Equal("partial", await ScalarAsync(connectionString, "SELECT status FROM askwerk_response"));

        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 5 } } }, Cancel)).EnsureSuccessStatusCode();
        Assert.Equal(1, await sync.RunAsync(survey, Cancel));

        Assert.Equal("5", await ScalarAsync(connectionString, "SELECT value FROM askwerk_answer WHERE variable = 'SAT'"));
        Assert.Equal(1, await ScalarAsync(connectionString, "SELECT count(*) FROM askwerk_response"));
        Assert.Equal(DBNull.Value, await ScalarAsync(connectionString, "SELECT last_error FROM askwerk_sync_state"));
    }

    private static async Task<string> ReadyDatabaseAsync(string server)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(server);
                await connection.OpenAsync(Cancel);
                await using var create = new SqlCommand("CREATE DATABASE reporting", connection);
                await create.ExecuteNonQueryAsync(Cancel);
                break;
            }
            catch (SqlException) when (attempt < 60)
            {
                await Task.Delay(1000, Cancel);
            }
        }

        var database = server + ";Database=reporting";
        await using var tables = new SqlConnection(database);
        await tables.OpenAsync(Cancel);
        await using var command = new SqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Examples", "create-tables.sql"), Cancel), tables);
        await command.ExecuteNonQueryAsync(Cancel);

        return database;
    }

    private static async Task<object?> ScalarAsync(string database, string query)
    {
        await using var connection = new SqlConnection(database);
        await connection.OpenAsync(Cancel);
        await using var command = new SqlCommand(query, connection);

        return await command.ExecuteScalarAsync(Cancel);
    }
}
