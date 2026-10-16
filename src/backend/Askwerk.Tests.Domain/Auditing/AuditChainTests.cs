// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Auditing;

namespace Askwerk.Tests.Domain.Auditing;

public sealed class AuditChainTests
{
    [Fact]
    public void Append_FirstEntry_LinksToGenesisHash()
    {
        var content = AuditContentBuilder.Default().Build();

        var entry = AuditChain.Append(previous: null, content);

        Assert.Equal(1, entry.Sequence);
        Assert.Equal(AuditChain.GenesisHash, entry.PreviousHash);
    }

    [Fact]
    public void Append_DifferentAction_ProducesDifferentHash()
    {
        var published = AuditContentBuilder.Default().WithAction("survey.published").Build();
        var closed = AuditContentBuilder.Default().WithAction("survey.closed").Build();

        var publishedEntry = AuditChain.Append(previous: null, published);
        var closedEntry = AuditChain.Append(previous: null, closed);

        Assert.NotEqual(publishedEntry.Hash, closedEntry.Hash);
    }

    [Fact]
    public void Verify_TimestampStoredWithMicrosecondPrecision_IsStillIntact()
    {
        // PostgreSQL keeps microseconds; .NET clocks have 100 ns ticks. A stored entry must still verify.
        var precise = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);
        var stored = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
        var entry = AuditChain.Append(previous: null, AuditContentBuilder.Default().At(precise).Build());
        var roundTripped = entry with { Content = entry.Content with { OccurredAt = stored } };

        var result = AuditChain.Verify([roundTripped]);

        Assert.True(result.IsIntact);
    }

    [Fact]
    public void Verify_UnchangedChain_IsIntact()
    {
        var chain = BuildChain(3);

        var result = AuditChain.Verify(chain);

        Assert.True(result.IsIntact);
        Assert.Null(result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_ChangedContent_ReportsBrokenAtThatEntry()
    {
        var chain = BuildChain(3);
        var tampered = chain[1] with { Content = chain[1].Content with { Action = "survey.deleted" } };
        var entries = new[] { chain[0], tampered, chain[2] };

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsIntact);
        Assert.Equal(2, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_RemovedEntry_ReportsBrokenWhereTheGapIs()
    {
        var chain = BuildChain(3);
        var entries = new[] { chain[0], chain[2] };

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsIntact);
        Assert.Equal(3, result.BrokenAtSequence);
    }

    private static AuditEntry[] BuildChain(int length)
    {
        var entries = new List<AuditEntry>();
        AuditEntry? previous = null;

        for (var index = 1; index <= length; index++)
        {
            var content = AuditContentBuilder.Default().WithEntityId($"entity-{index}").Build();
            previous = AuditChain.Append(previous, content);
            entries.Add(previous);
        }

        return entries.ToArray();
    }
}
