// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Infrastructure.Persistence;
using Npgsql;

namespace Askwerk.Tests.Integration;

/// <summary>A fresh, migrated database with owner and app-role connection strings.</summary>
internal sealed record TestDatabase(string OwnerConnectionString, string AppConnectionString)
{
    public const string DataKey = "dGVzdC1kYXRhLWtleS1ub3QtYS1zZWNyZXQtMzItYnl0ZXM=";

    public static async Task<TestDatabase> CreateMigratedAsync(PostgresServer server, CancellationToken cancellationToken)
    {
        var owner = await server.CreateDatabaseAsync();
        var app = await DatabaseBootstrapper.EnsureReadyAsync(owner, DataKey, cancellationToken);

        return new TestDatabase(owner, app);
    }

    public AskwerkDbContext CreateAppContext() => AskwerkDbContext.Create(AppConnectionString);

    public AskwerkDbContext CreateAppContext(DbSessionContext session) => AskwerkDbContext.Create(AppConnectionString, session);

    public Task<int> ExecuteAsOwnerAsync(string sql) => ExecuteAsync(OwnerConnectionString, sql);

    public Task<int> ExecuteAsAppAsync(string sql) => ExecuteAsync(AppConnectionString, sql);

    private static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteNonQueryAsync();
    }
}
