// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Auditing;

/// <summary>One link in a tenant's (or the instance's) audit hash chain.</summary>
/// <param name="Sequence">Position in the chain, starting at 1.</param>
/// <param name="Content">The audited facts.</param>
/// <param name="PreviousHash">Hash of the previous entry, or <see cref="AuditChain.GenesisHash"/> for the first.</param>
/// <param name="Hash">Hash over sequence, content and previous hash.</param>
public sealed record AuditEntry(long Sequence, AuditContent Content, string PreviousHash, string Hash);
