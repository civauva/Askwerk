// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Askwerk.Tests.Integration.Persistence;

/// <summary>
/// The FullAccessContacts migration (AWK-67): "Full access" has every scope, also for endpoints added later, so keys and
/// OAuth clients made with it before the contacts scopes get them; every other key keeps what it had.
/// </summary>
public sealed class FullAccessContactsMigrationTests(PostgresServer server)
{
    private const string Previous = "20261017133918_HostLook";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] OldFullAccess = ["surveys:read", "surveys:write", "responses:read", "responses:write", "participants:read", "participants:write", "webhooks:manage"];
    private static readonly string[] OldFullAccessWithUsers = [.. OldFullAccess, "users:manage"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_GivesFullAccessKeysAndClientsTheContactsScopes_AndNoOtherKey()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        var tenantId = Guid.CreateVersion7(Now);
        var userId = Guid.CreateVersion7(Now);
        var full = ApiKey.Issue(Guid.CreateVersion7(Now), tenantId, "HR sync", OldFullAccess, null, userId, Now).Key;
        var fullWithUsers = ApiKey.Issue(Guid.CreateVersion7(Now), tenantId, "HR provisioning", OldFullAccessWithUsers, null, userId, Now).Key;
        var reading = ApiKey.Issue(Guid.CreateVersion7(Now), tenantId, "Reporting server", ApiScopes.ReadResults, null, userId, Now).Key;
        var client = OAuthClient.Issue(Guid.CreateVersion7(Now), tenantId, "CRM", OldFullAccess, null, userId, Now).Client;

        await using (var context = AskwerkDbContext.Create(database.OwnerConnectionString))
        {
            context.Tenants.Add(Tenant.Create(tenantId, "Acme GmbH", "acme", Now));
            context.Users.Add(User.Create(userId, "anna@acme.at", "Anna Berger", Now));
            context.ApiKeys.AddRange(full, fullWithUsers, reading);
            context.OAuthClients.Add(client);
            await context.SaveChangesAsync(Cancel);
            await context.GetService<IMigrator>().MigrateAsync(Previous, cancellationToken: Cancel);
        }

        await using (var context = AskwerkDbContext.Create(database.OwnerConnectionString))
        {
            await context.Database.MigrateAsync(Cancel);
        }

        await using var after = AskwerkDbContext.Create(database.OwnerConnectionString);
        var keys = await after.ApiKeys.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Scopes, Cancel);
        var clientScopes = await after.OAuthClients.AsNoTracking().Where(row => row.Id == client.Id).Select(row => row.Scopes).SingleAsync(Cancel);

        Assert.Equal(ApiScopes.FullAccess, keys[full.Id]);
        Assert.Equal(ApiScopes.Allowed, keys[fullWithUsers.Id]);
        Assert.Equal(ApiScopes.ReadResults, keys[reading.Id]);
        Assert.Equal(ApiScopes.FullAccess, clientScopes);
    }
}
