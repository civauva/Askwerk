// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Instances;
using Askwerk.Application.Setup;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Tests.Integration.Identity;

/// <summary>Setup against the real database: one transaction, accepted by row-level security, audited on both chains.</summary>
public sealed class SetupPersistenceTests(PostgresServer server)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 21, 45, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly CompleteSetupCommand Command = new(
        "Anna Berger", "anna@acme.at", "correct horse battery", "Acme Surveys", "https://surveys.acme.at", "de", "Europe/Vienna", "Acme GmbH", "acme");

    [Fact]
    public async Task CompleteSetup_RealDatabase_StoresEverythingTheOwnerCanSee()
    {
        var database = await InitializedAsync();

        var result = await CompleteSetupAsync(database);

        await using var context = database.CreateAppContext(new DbSessionContext { UserId = result.UserId });
        var tenants = await context.Tenants.Select(tenant => tenant.Slug).ToListAsync(Cancel);
        var settings = await new EfInstanceSettingsRepository(context).FindAsync(Cancel);
        var owner = await context.Users.SingleAsync(Cancel);
        Assert.Equal(["acme"], tenants);
        Assert.Equal(("Acme Surveys", result.UserId), (settings!.Name, settings.OwnerUserId));
        Assert.Equal("hashed", owner.PasswordHash);
    }

    [Fact]
    public async Task CompleteSetup_RealDatabase_WritesIntactAuditChainsForInstanceAndTenant()
    {
        var database = await InitializedAsync();

        var result = await CompleteSetupAsync(database);

        await using var context = database.CreateAppContext();
        var instanceChain = await new AuditLogReader(context).ReadChainAsync(null, Cancel);
        var tenantChain = await new AuditLogReader(context).ReadChainAsync(result.TenantId, Cancel);
        Assert.Equal(["instance.initialized", "setup.completed", "user.created"], instanceChain.Select(entry => entry.Content.Action));
        Assert.Equal(["tenant.created", "membership.created"], tenantChain.Select(entry => entry.Content.Action));
        Assert.True(AuditChain.Verify(instanceChain).IsIntact && AuditChain.Verify(tenantChain).IsIntact);
    }

    [Fact]
    public async Task SlugTaken_AnotherTenantsSlug_IsSeenThroughRowLevelSecurity()
    {
        var database = await InitializedAsync();
        await CompleteSetupAsync(database);

        await using var context = database.CreateAppContext(new DbSessionContext());
        var repository = new EfTenantRepository(context);

        Assert.True(await repository.SlugTakenAsync("acme", Cancel));
        Assert.False(await repository.SlugTakenAsync("acme-gmbh", Cancel));
    }

    private async Task<TestDatabase> InitializedAsync()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, Cancel);
        await using var context = database.CreateAppContext();
        var instances = new EfInstanceRepository(context);
        await new InitializeInstanceHandler(instances, new EfAuditWriter(context), new EfUnitOfWork(context), new FixedTimeProvider(Now), new NoTraceContext())
            .HandleAsync(Cancel);

        return database;
    }

    private static async Task<SetupResult> CompleteSetupAsync(TestDatabase database)
    {
        var session = new DbSessionContext();
        await using var context = database.CreateAppContext(session);
        var handler = new CompleteSetupHandler(
            new EfInstanceRepository(context),
            new EfInstanceSettingsRepository(context),
            new EfTenantRepository(context),
            new PlainAccounts(new EfUserRepository(context)),
            new PlainSecrets(),
            new DbDataScope(session),
            new EfAuditWriter(context),
            new EfUnitOfWork(context),
            new FixedTimeProvider(Now),
            new NoTraceContext());

        return await handler.HandleAsync(Command, Cancel);
    }

    private sealed class PlainSecrets : ISecretProtector
    {
        public string Protect(string secret) => secret;

        public string Unprotect(string protectedSecret) => protectedSecret;
    }

    /// <summary>Stands in for the Identity-backed accounts of the API; hashing is tested there.</summary>
    private sealed class PlainAccounts(EfUserRepository users) : IAccounts
    {
        public Task<string?> CreateAsync(User user, string password, CancellationToken cancellationToken)
        {
            user.PasswordHash = "hashed";
            users.Add(user);

            return Task.FromResult<string?>(null);
        }
    }
}
