// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

public sealed class StatusAndAuditTests(PostgresServer server)
{
    [Fact]
    public async Task Status_DatabaseReady_ReportsVersionAndReady()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = factory.CreateClient();
        await factory.WaitUntilReadyAsync(client);

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/app/status", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("0.0.0-test", status.GetProperty("version").GetString());
        Assert.Equal("ready", status.GetProperty("database").GetString());
        Assert.Equal(JsonValueKind.String, status.GetProperty("startedAt").ValueKind);
    }

    [Fact]
    public async Task Status_DatabaseUnreachable_ReportsNotReady()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/app/status", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("notReady", status.GetProperty("database").GetString());
    }

    [Fact]
    public async Task FirstStart_AuditsTheInstanceOnce()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = factory.CreateClient();
        await factory.WaitUntilReadyAsync(client);

        var actions = await AuditActionsAsync(factory.OwnerConnectionString);

        Assert.Equal(["instance.initialized"], actions);
    }

    [Fact]
    public async Task AuditVerify_NotSignedIn_IsUnauthorized()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = factory.CreateClient();
        await factory.WaitUntilReadyAsync(client);

        var response = await client.GetAsync(new Uri("/api/app/audit/verify", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FirstStart_SecondStartOnSameDatabase_DoesNotAuditAgain()
    {
        var owner = await server.CreateDatabaseAsync(migrated: false);
        var dataDirectory = Path.Combine(Path.GetTempPath(), "askwerk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        await using (var first = new AskwerkApiFactory(owner, dataDirectory))
        {
            using var firstClient = first.CreateClient();
            await first.WaitUntilReadyAsync(firstClient);
        }

        await using var second = new AskwerkApiFactory(owner, dataDirectory);
        using var client = second.CreateClient();
        await second.WaitUntilReadyAsync(client);
        var actions = await AuditActionsAsync(owner);

        Assert.Equal(["instance.initialized"], actions);
    }

    /// <summary>The audit actions as stored, read as the database owner.</summary>
    private static async Task<List<string>> AuditActionsAsync(string ownerConnectionString)
    {
        await using var connection = new Npgsql.NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("SELECT action FROM audit_log ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var actions = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            actions.Add(reader.GetString(0));
        }

        return actions;
    }

    [Fact]
    public async Task UnknownAppRoute_Returns404ProblemDetailsWithTraceId()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/app/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }
}
