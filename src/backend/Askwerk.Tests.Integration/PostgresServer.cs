// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using Askwerk.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Askwerk.Tests.Integration.PostgresServer))]

namespace Askwerk.Tests.Integration;

/// <summary>
/// One PostgreSQL server for the whole test run: an existing one from ASKWERK_TEST_DB (an owner connection
/// string whose role may create databases and roles), or a throwaway container. Migrations run once, into a
/// template database; every test gets its own copy of it (or an empty database), and all of them are dropped
/// at the end, so a local server stays clean.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    // Each API test starts its own app with its own pools; small pools keep a default server (100 connections) enough.
    private const int PoolSize = 8;

    // The tests' data key (see TestDatabase and AskwerkApiFactory); the template's app role password derives from it.
    private const string DataKey = "dGVzdC1kYXRhLWtleS1ub3QtYS1zZWNyZXQtMzItYnl0ZXM=";

    private readonly string _run = Guid.NewGuid().ToString("N")[..12];
    private readonly ConcurrentBag<string> _databases = [];
    private PostgreSqlContainer? _container;
    private string _serverConnectionString = "";
    private string _template = "";

    public async ValueTask InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable("ASKWERK_TEST_DB");

        if (!string.IsNullOrWhiteSpace(existing))
        {
            _serverConnectionString = existing;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").WithCommand("-c", "max_connections=500").Build();
            await _container.StartAsync();
            _serverConnectionString = _container.GetConnectionString();
        }

        _template = await CreateDatabaseAsync(migrated: false, template: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();

        foreach (var name in _databases)
        {
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>A fresh database and an owner connection string for it: a copy of the migrated template, or empty.</summary>
    /// <param name="migrated">Copy the migrated template (fast); false gives an empty database, for tests of first start itself.</param>
    public Task<string> CreateDatabaseAsync(bool migrated = true) => CreateDatabaseAsync(migrated, template: false);

    private async Task<string> CreateDatabaseAsync(bool migrated, bool template)
    {
        var name = $"askwerk_test_{_run}_{(template ? "template" : Guid.NewGuid().ToString("N"))}";
        var source = migrated ? $" TEMPLATE {new NpgsqlConnectionStringBuilder(_template).Database}" : "";
        _databases.Add(name);

        await using (var connection = new NpgsqlConnection(_serverConnectionString))
        {
            await connection.OpenAsync();

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await using var command = new NpgsqlCommand($"CREATE DATABASE {name}{source}", connection);
                    await command.ExecuteNonQueryAsync();
                    break;
                }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ObjectInUse && attempt < 20)
                {
                    // Copies of the template run one at a time on some servers ("source database is being accessed").
                    await Task.Delay(25 * attempt);
                }
            }
        }

        // Every test database has its own pools, which outlive the test; idle connections close after a few seconds,
        // so a full run stays under the server's connection limit (the app role's string inherits these settings).
        var owner = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = name, MaxPoolSize = PoolSize, ConnectionIdleLifetime = 5, ConnectionPruningInterval = 2 }.ConnectionString;

        if (template)
        {
            // Without pooling, so no connection stays open on the template; copies need it unused.
            var migrator = new NpgsqlConnectionStringBuilder(owner) { Pooling = false }.ConnectionString;
            await DatabaseBootstrapper.EnsureReadyAsync(migrator, DataKey, CancellationToken.None);
        }

        return owner;
    }
}
