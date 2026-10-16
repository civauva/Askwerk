// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Auditing;

/// <summary>The kind of actor behind an audit entry. Respondents are never actors (agents/observability.md).</summary>
public enum AuditActorKind
{
    /// <summary>Askwerk itself, e.g. during first start.</summary>
    System = 0,

    /// <summary>A signed-in user.</summary>
    User = 1,

    /// <summary>An integration using an API key.</summary>
    ApiKey = 2,

    /// <summary>An integration using OAuth client credentials.</summary>
    OAuthClient = 3,

    /// <summary>A background job.</summary>
    Job = 4,
}
