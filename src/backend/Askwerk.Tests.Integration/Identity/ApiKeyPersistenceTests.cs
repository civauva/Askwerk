// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Tests.Integration.Identity;

/// <summary>API keys (AWK-25): kept per organisation, found by their prefix before the organisation is known.</summary>
public sealed class ApiKeyPersistenceTests(PostgresServer server)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid AcmeId = Guid.Parse("0199e000-0000-7000-8000-00000000000a");
    private static readonly Guid OtherId = Guid.Parse("0199e000-0000-7000-8000-00000000000b");
    private static readonly Guid AnnaId = Guid.Parse("0199e000-0000-7000-8000-0000000000a1");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApiKeys_InATenantContext_ShowOnlyThatTenant()
    {
        var (database, acme, _) = await SeededAsync();

        await using var context = database.CreateAppContext(new DbSessionContext { TenantId = AcmeId });
        var keys = await context.ApiKeys.ToListAsync(Cancel);

        Assert.Equal([acme.Key.Id], keys.Select(key => key.Id));
        Assert.Equal(["surveys:read", "responses:read"], keys[0].Scopes);
    }

    [Fact]
    public async Task Lookup_ByPrefix_FindsTheKeyAndItsOrganisationWithoutATenant()
    {
        var (database, acme, other) = await SeededAsync();

        await using var context = database.CreateAppContext(new DbSessionContext());
        var found = await context.FindApiKeyAsync(other.Secret[..12], Cancel);
        var missing = await context.FindApiKeyAsync("awk_00000000", Cancel);

        Assert.NotNull(found);
        Assert.Equal((other.Key.Id, OtherId), (found.Id, found.TenantId));
        Assert.True(found.Matches(other.Secret));
        Assert.False(found.Matches(acme.Secret));
        Assert.Null(missing);
    }

    /// <summary>Acme and Other, each with one key; seeded as the database owner, which RLS does not restrict.</summary>
    private async Task<(TestDatabase Database, (ApiKey Key, string Secret) Acme, (ApiKey Key, string Secret) Other)> SeededAsync()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        await using var context = AskwerkDbContext.Create(database.OwnerConnectionString);
        context.Tenants.AddRange(Tenant.Create(AcmeId, "Acme GmbH", "acme", Now), Tenant.Create(OtherId, "Other AG", "other", Now));
        context.Users.Add(User.Create(AnnaId, "anna@acme.at", "Anna Berger", Now));
        var acme = ApiKey.Issue(Guid.CreateVersion7(Now), AcmeId, "Reporting server", ApiScopes.ReadResults, null, AnnaId, Now);
        var other = ApiKey.Issue(Guid.CreateVersion7(Now), OtherId, "HR sync", ApiScopes.FullAccess, null, AnnaId, Now);
        context.ApiKeys.AddRange(acme.Key, other.Key);
        await context.SaveChangesAsync(Cancel);

        return (database, acme, other);
    }
}
