// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Infrastructure.Persistence;
using Npgsql;

namespace Askwerk.Tests.Integration.Persistence;

public sealed class DatabaseBootstrapperTests(PostgresServer server)
{
    [Fact]
    public async Task EnsureReadyAsync_EmptyDatabase_AppRoleCanReadTheSchema()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);

        await using var context = database.CreateAppContext();
        var instance = await new EfInstanceRepository(context).FindAsync(TestContext.Current.CancellationToken);

        Assert.Null(instance);
    }

    [Fact]
    public async Task EnsureReadyAsync_RunTwice_ReturnsTheSameAppConnection()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);

        var app = await DatabaseBootstrapper.EnsureReadyAsync(database.OwnerConnectionString, TestDatabase.DataKey, TestContext.Current.CancellationToken);

        Assert.Equal(database.AppConnectionString, app);
    }

    [Fact]
    public async Task EnsureReadyAsync_AppConnection_UsesTheAppRoleNotTheOwner()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);

        var builder = new NpgsqlConnectionStringBuilder(database.AppConnectionString);

        Assert.Equal("askwerk_app", builder.Username);
    }

    [Fact]
    public async Task EnsureReadyAsync_ManyDatabasesAtOnce_AllSucceed()
    {
        // The app role is server-wide; preparing several databases in parallel updates it concurrently.
        var owners = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.CreateDatabaseAsync(migrated: false)));

        var apps = await Task.WhenAll(owners.Select(owner => DatabaseBootstrapper.EnsureReadyAsync(owner, TestDatabase.DataKey, TestContext.Current.CancellationToken)));

        Assert.Equal(8, apps.Length);
    }
}
