// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Askwerk.Domain.Auditing;

/// <summary>Builds and verifies the tamper-evident hash chain of audit entries.</summary>
public static class AuditChain
{
    /// <summary>The previous hash of the first entry in every chain.</summary>
    public const string GenesisHash = "";

    /// <summary>Creates the entry that follows <paramref name="previous"/> in the chain.</summary>
    /// <param name="previous">The last entry of the chain, or null when the chain is empty.</param>
    /// <param name="content">The facts to audit.</param>
    /// <returns>The new entry, linked to the previous one.</returns>
    public static AuditEntry Append(AuditEntry? previous, AuditContent content)
    {
        var sequence = previous is null ? 1 : previous.Sequence + 1;
        var previousHash = previous is null ? GenesisHash : previous.Hash;
        var hash = ComputeHash(sequence, content, previousHash);

        return new AuditEntry(sequence, content, previousHash, hash);
    }

    /// <summary>Checks that every entry matches its hash and links to the entry before it.</summary>
    /// <param name="entries">The chain in sequence order, starting with the first entry.</param>
    /// <returns>Whether the chain is intact, and if not, where it breaks.</returns>
    public static AuditChainVerification Verify(IReadOnlyList<AuditEntry> entries)
    {
        AuditEntry? previous = null;

        foreach (var entry in entries)
        {
            var expected = Append(previous, entry.Content);

            if (entry != expected)
            {
                return new AuditChainVerification(IsIntact: false, BrokenAtSequence: entry.Sequence);
            }

            previous = entry;
        }

        return new AuditChainVerification(IsIntact: true, BrokenAtSequence: null);
    }

    private static string ComputeHash(long sequence, AuditContent content, string previousHash)
    {
        // A JSON array gives an unambiguous, order-stable encoding of the fields; changing this
        // encoding breaks every existing chain, so it needs a migration and a decisions.md entry.
        var fields = new object?[]
        {
            sequence,
            ToMicroseconds(content.OccurredAt),
            content.TenantId,
            content.ActorKind.ToString(),
            content.ActorId,
            content.Action,
            content.EntityType,
            content.EntityId,
            content.Diff,
            content.TraceId,
            previousHash,
        };

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fields));

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // PostgreSQL stores timestamps with microsecond precision, so the hash may only use that much.
    private static string ToMicroseconds(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var truncated = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));

        return truncated.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }
}
