// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Auditing;

/// <summary>Result of checking an audit hash chain.</summary>
/// <param name="IsIntact">True when every entry matches its hash and links to its predecessor.</param>
/// <param name="BrokenAtSequence">The first entry that does not match, or null when intact.</param>
public sealed record AuditChainVerification(bool IsIntact, long? BrokenAtSequence);
