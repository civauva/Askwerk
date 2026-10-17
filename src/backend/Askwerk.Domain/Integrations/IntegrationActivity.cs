// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Integrations;

/// <summary>
/// How much an API key or OAuth client was used on one day (W36): its calls, and the calls Askwerk refused (paused,
/// expired, revoked, or without the scope). Written by the database function note_integration_activity, read by
/// Connections for its health column.
/// </summary>
public sealed class IntegrationActivity
{
    /// <summary>Gets the organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the API key or OAuth client.</summary>
    public Guid CredentialId { get; private set; }

    /// <summary>Gets the day (UTC).</summary>
    public DateOnly Day { get; private set; }

    /// <summary>Gets the calls that were let in.</summary>
    public int Calls { get; private set; }

    /// <summary>Gets the calls that were refused.</summary>
    public int Refused { get; private set; }
}
