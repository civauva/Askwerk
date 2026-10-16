// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

public sealed class HealthTests(PostgresServer server)
{
    [Fact]
    public async Task Live_ProcessRunning_Returns200()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_DatabaseUnreachable_Returns503()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Ready_DatabaseMigrated_Returns200()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = factory.CreateClient();

        await factory.WaitUntilReadyAsync(client);
        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
