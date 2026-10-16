// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Identity;

/// <summary>What a member may do in a tenant (agents/data-model.md).</summary>
public enum TenantRole
{
    /// <summary>Everything, including deleting the tenant.</summary>
    Owner = 0,

    /// <summary>Everything except deleting the tenant.</summary>
    Admin = 1,

    /// <summary>Creates and edits surveys.</summary>
    Editor = 2,

    /// <summary>Reads results and exports.</summary>
    Analyst = 3,

    /// <summary>Reads only.</summary>
    Viewer = 4,
}

/// <summary>Rules that depend on the role.</summary>
public static class TenantRoleRules
{
    /// <summary>Owners and admins must sign in with two-factor (Dražan, 2026-10-13, AWK-16).</summary>
    /// <param name="role">The role.</param>
    /// <returns>True when two-factor is required.</returns>
    public static bool RequiresTwoFactor(this TenantRole role) => role is TenantRole.Owner or TenantRole.Admin;
}

/// <summary>A user's role in a tenant; workspace null means the whole tenant.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="UserId">The member.</param>
/// <param name="TenantId">The tenant.</param>
/// <param name="WorkspaceId">A workspace inside the tenant, or null for all of it.</param>
/// <param name="Role">The role.</param>
/// <param name="CreatedAt">When the membership was created (UTC).</param>
public sealed record Membership(Guid Id, Guid UserId, Guid TenantId, Guid? WorkspaceId, TenantRole Role, DateTimeOffset CreatedAt);
