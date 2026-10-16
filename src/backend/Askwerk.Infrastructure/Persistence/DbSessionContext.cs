// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Infrastructure.Persistence;

/// <summary>
/// Who the current request acts for, as the database sees it: set from the authenticated caller only,
/// never from request input, and passed to PostgreSQL for row-level security (agents/architecture.md).
/// </summary>
public sealed class DbSessionContext
{
    /// <summary>The tenant the request works in, if any.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The signed-in user, if any.</summary>
    public Guid? UserId { get; set; }
}
