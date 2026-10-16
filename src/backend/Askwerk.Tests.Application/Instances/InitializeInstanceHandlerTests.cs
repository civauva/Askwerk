// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Instances;
using Askwerk.Domain.Auditing;
using Askwerk.Tests.Application.Fakes;

namespace Askwerk.Tests.Application.Instances;

public sealed class InitializeInstanceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_NoInstanceYet_CreatesInstanceAndAuditsIt()
    {
        var instances = new InMemoryInstanceRepository();
        var audit = new RecordingAuditWriter();
        var handler = new InitializeInstanceHandler(instances, audit, new InMemoryUnitOfWork(instances, audit), new FixedTimeProvider(Now), new FixedTraceContext("4bf92f3577b34da6a3ce929d0e0e4736"));

        await handler.HandleAsync(TestContext.Current.CancellationToken);

        var instance = Assert.Single(instances.Saved);
        Assert.Equal(Now, instance.InitializedAt);
        var entry = Assert.Single(audit.Written);
        Assert.Equal("instance.initialized", entry.Action);
        Assert.Equal(AuditActorKind.System, entry.ActorKind);
        Assert.Null(entry.TenantId);
        Assert.Equal("Instance", entry.EntityType);
        Assert.Equal(instance.Id.ToString(), entry.EntityId);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", entry.TraceId);
    }

    [Fact]
    public async Task HandleAsync_AlreadyInitialized_ChangesNothing()
    {
        var instances = new InMemoryInstanceRepository();
        var audit = new RecordingAuditWriter();
        var handler = new InitializeInstanceHandler(instances, audit, new InMemoryUnitOfWork(instances, audit), new FixedTimeProvider(Now), new FixedTraceContext(null));
        await handler.HandleAsync(TestContext.Current.CancellationToken);
        audit.Written.Clear();

        await handler.HandleAsync(TestContext.Current.CancellationToken);

        Assert.Single(instances.Saved);
        Assert.Empty(audit.Written);
    }
}
