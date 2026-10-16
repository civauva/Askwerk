// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Instances;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Instances;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Npgsql;

namespace Askwerk.Tests.Integration.Auditing;

public sealed class AuditPersistenceTests(PostgresServer server)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeInstance_StoresInstanceAndAnIntactAuditChain()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);

        await InitializeAsync(database);

        await using var context = database.CreateAppContext();
        var instance = await new EfInstanceRepository(context).FindAsync(TestContext.Current.CancellationToken);
        var chain = await new AuditLogReader(context).ReadChainAsync(tenantId: null, TestContext.Current.CancellationToken);
        Assert.NotNull(instance);
        var entry = Assert.Single(chain);
        Assert.Equal("instance.initialized", entry.Content.Action);
        Assert.True(AuditChain.Verify(chain).IsIntact);
    }

    [Fact]
    public async Task SaveChanges_ChangeFails_NoAuditEntryIsStored()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);
        await InitializeAsync(database);
        await using var context = database.CreateAppContext();
        var existing = await new EfInstanceRepository(context).FindAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        new EfInstanceRepository(context).Add(new Instance(existing!.Id, Now));
        await new EfAuditWriter(context).WriteAsync(ContentFor("instance.duplicated"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => new EfUnitOfWork(context).SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var fresh = database.CreateAppContext();
        var chain = await new AuditLogReader(fresh).ReadChainAsync(tenantId: null, TestContext.Current.CancellationToken);
        Assert.Single(chain);
    }

    [Theory]
    [InlineData("UPDATE audit_log SET action = 'x'")]
    [InlineData("DELETE FROM audit_log")]
    [InlineData("TRUNCATE audit_log")]
    public async Task AppRole_ChangingAuditLog_IsDeniedByTheDatabase(string sql)
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);
        await InitializeAsync(database);

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsAppAsync(sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task ReadChain_AfterTamperingInTheDatabase_VerifyReportsBroken()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);
        await InitializeAsync(database);
        await database.ExecuteAsOwnerAsync("UPDATE audit_log SET action = 'instance.forged'");

        await using var context = database.CreateAppContext();
        var chain = await new AuditLogReader(context).ReadChainAsync(tenantId: null, TestContext.Current.CancellationToken);

        var result = AuditChain.Verify(chain);
        Assert.False(result.IsIntact);
        Assert.Equal(1, result.BrokenAtSequence);
    }

    [Fact]
    public async Task WriteAsync_SeveralEntriesInOneSave_FormOneIntactChain()
    {
        var database = await TestDatabase.CreateMigratedAsync(server, TestContext.Current.CancellationToken);
        await using var context = database.CreateAppContext();
        var audit = new EfAuditWriter(context);
        await audit.WriteAsync(ContentFor("first"), TestContext.Current.CancellationToken);
        await audit.WriteAsync(ContentFor("second"), TestContext.Current.CancellationToken);

        await new EfUnitOfWork(context).SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var fresh = database.CreateAppContext();
        var chain = await new AuditLogReader(fresh).ReadChainAsync(tenantId: null, TestContext.Current.CancellationToken);
        Assert.Equal(2, chain.Count);
        Assert.True(AuditChain.Verify(chain).IsIntact);
    }

    private static async Task InitializeAsync(TestDatabase database)
    {
        await using var context = database.CreateAppContext();
        var handler = new InitializeInstanceHandler(
            new EfInstanceRepository(context),
            new EfAuditWriter(context),
            new EfUnitOfWork(context),
            new FixedTimeProvider(Now),
            new NoTraceContext());

        await handler.HandleAsync(TestContext.Current.CancellationToken);
    }

    private static AuditContent ContentFor(string action) => new(
        OccurredAt: Now,
        TenantId: null,
        ActorKind: AuditActorKind.System,
        ActorId: null,
        Action: action,
        EntityType: "Instance",
        EntityId: "instance",
        Diff: null,
        TraceId: null);
}
