// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Email;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Members;

/// <summary>
/// Members of the organisation chosen in the session (AWK-30, W18). Owners and admins invite, change roles and remove;
/// admins never touch owners; there is always an owner; anyone but the last owner may leave. Every step is audited.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/members")]
public sealed class MembersController(
    AskwerkDbContext context,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace,
    ISecretProtector secrets,
    EmailSettingsProvider email,
    IEmailSender sender,
    UserManager<User> users,
    SessionService sessions,
    IInstanceSettingsRepository instance) : ControllerBase
{
    /// <summary>The members and the open invitations.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Both lists; 403 for editors, analysts and viewers.</returns>
    [HttpGet]
    [ProducesResponseType<MembersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var role = await RoleAsync(cancellationToken);

        if (role is not (TenantRole.Owner or TenantRole.Admin))
        {
            return Forbid();
        }

        var tenantId = User.TenantId()!.Value;
        var me = User.UserId();
        var members = await context.Memberships.AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .Join(context.Users, membership => membership.UserId, user => user.Id, (membership, user) => new { membership.Role, user })
            .Select(row => new
            {
                row.Role,
                row.user.Id,
                row.user.DisplayName,
                row.user.Email,
                row.user.LastSignInAt,
                TwoFactor = row.user.TwoFactorEnabled || context.Passkeys.Any(passkey => passkey.UserId == row.user.Id),
            })
            .ToListAsync(cancellationToken);
        var managed = await context.ExternalIdentities.AsNoTracking().Where(row => row.TenantId == tenantId && row.Active).Select(row => row.UserId).ToListAsync(cancellationToken);
        var systemName = await context.TenantSignIns.AsNoTracking().Where(row => row.TenantId == tenantId).Select(row => row.SystemName).FirstOrDefaultAsync(cancellationToken) ?? "";
        var invitations = await context.MemberInvitations.AsNoTracking()
            .Where(invitation => invitation.TenantId == tenantId)
            .OrderByDescending(invitation => invitation.InvitedAt)
            .Select(invitation => new { invitation, By = context.Users.Where(user => user.Id == invitation.InvitedBy).Select(user => user.DisplayName).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        return Ok(new MembersResponse(
            [.. members
                .OrderBy(member => member.Role)
                .ThenBy(member => member.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(member => new MemberItem(member.Id, member.DisplayName, member.Email, Name(member.Role), member.TwoFactor, member.LastSignInAt, member.Id == me, managed.Contains(member.Id) && member.Role != TenantRole.Owner ? systemName : null))],
            [.. invitations.Select(row => new InvitationItem(row.invitation.Id, row.invitation.Email, Name(row.invitation.Role), row.By ?? "", row.invitation.InvitedAt, row.invitation.ExpiresAt, row.invitation.SentAt, row.invitation.IsOpen(time.GetUtcNow())))],
            role == TenantRole.Owner,
            await email.FindAsync(cancellationToken) is not null));
    }

    /// <summary>Invites addresses with one role; sends the links when email is set up.</summary>
    /// <param name="request">The addresses and the role.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new invitations with their links, and the addresses that are members already.</returns>
    [HttpPost("invitations")]
    [ProducesResponseType<InvitedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Invite(InviteMembersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var role = await RoleAsync(cancellationToken);

        if (ParseRole(request.Role) is not { } invitedRole)
        {
            ModelState.AddModelError("role", "role-invalid");
            return ValidationProblem(ModelState);
        }

        if (!MayManage(role, invitedRole))
        {
            return Forbid();
        }

        var addresses = (request.Emails ?? []).Select(MemberInvitation.Normalise).Where(address => address.Length > 0).Distinct().ToList();

        if (addresses.Count is 0 or > 100)
        {
            ModelState.AddModelError("emails", addresses.Count == 0 ? "emails-required" : "emails-too-many");
            return ValidationProblem(ModelState);
        }

        var tenantId = User.TenantId()!.Value;
        var now = time.GetUtcNow();
        var members = await context.Memberships
            .Where(membership => membership.TenantId == tenantId)
            .Join(context.Users, membership => membership.UserId, user => user.Id, (membership, user) => user.NormalizedEmail)
            .ToListAsync(cancellationToken);
        var already = addresses.Where(address => members.Contains(address.ToUpperInvariant())).ToList();
        var settings = await email.FindAsync(cancellationToken);
        var baseAddress = settings?.BaseAddress ?? await BaseAddressAsync(cancellationToken);
        var created = new List<(MemberInvitation Invitation, string Link)>();

        // One open invitation per address: a new one replaces the old.
        var replaced = await context.MemberInvitations.Where(invitation => invitation.TenantId == tenantId && addresses.Contains(invitation.Email)).ToListAsync(cancellationToken);
        context.MemberInvitations.RemoveRange(replaced);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var address in addresses.Except(already))
        {
            var token = MemberInvitation.NewToken();
            var invitation = MemberInvitation.Create(Guid.CreateVersion7(now), tenantId, address, invitedRole, token, secrets.Protect(token), User.UserId()!.Value, now);
            context.MemberInvitations.Add(invitation);
            created.Add((invitation, Link(baseAddress, token)));
            await AuditAsync(tenantId, "member.invited", "MemberInvitation", invitation.Id, new { email = address, role = Name(invitedRole) }, cancellationToken);
        }

        var sent = false;

        if (settings is { } found && created.Count > 0)
        {
            var organisation = await context.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.Name).FirstAsync(cancellationToken);
            var inviter = await context.Users.Where(user => user.Id == User.UserId()).Select(user => user.DisplayName).FirstAsync(cancellationToken);
            sent = true;

            foreach (var (invitation, link) in created)
            {
                try
                {
                    await sender.SendAsync(found.Smtp, EmailTexts.MemberInvitation(invitation.Email, organisation, inviter, link, found.Language), cancellationToken);
                    invitation.MarkSent(now);
                }
                catch (EmailRejectedException)
                {
                    sent = false;
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new InvitedResponse([.. created.Select(item => new CreatedInvitation(item.Invitation.Id, item.Invitation.Email, item.Link))], already, sent));
    }

    /// <summary>The link of an open invitation, to copy.</summary>
    /// <param name="invitationId">The invitation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The link; 403; 404.</returns>
    [HttpGet("invitations/{invitationId:guid}/link")]
    [ProducesResponseType<InvitationLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InvitationLink(Guid invitationId, CancellationToken cancellationToken)
    {
        var (invitation, refused) = await ManagedInvitationAsync(invitationId, cancellationToken);

        return refused ?? Ok(new InvitationLinkResponse(Link(await BaseAddressAsync(cancellationToken), secrets.Unprotect(invitation!.TokenProtected))));
    }

    /// <summary>Sends an open invitation again.</summary>
    /// <param name="invitationId">The invitation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404; 409 email-not-set-up.</returns>
    [HttpPost("invitations/{invitationId:guid}/send")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<MemberProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendAgain(Guid invitationId, CancellationToken cancellationToken)
    {
        var (invitation, refused) = await ManagedInvitationAsync(invitationId, cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        if (await email.FindAsync(cancellationToken) is not { } found)
        {
            return Conflict(new MemberProblem("email-not-set-up"));
        }

        var organisation = await context.Tenants.Where(tenant => tenant.Id == invitation!.TenantId).Select(tenant => tenant.Name).FirstAsync(cancellationToken);
        var inviter = await context.Users.Where(user => user.Id == User.UserId()).Select(user => user.DisplayName).FirstAsync(cancellationToken);
        await sender.SendAsync(found.Smtp, EmailTexts.MemberInvitation(invitation!.Email, organisation, inviter, Link(found.BaseAddress, secrets.Unprotect(invitation.TokenProtected)), found.Language), cancellationToken);
        invitation.MarkSent(time.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Revokes an invitation; its link stops working.</summary>
    /// <param name="invitationId">The invitation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpDelete("invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid invitationId, CancellationToken cancellationToken)
    {
        var (invitation, refused) = await ManagedInvitationAsync(invitationId, cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        context.MemberInvitations.Remove(invitation!);
        await AuditAsync(invitation!.TenantId, "invitation.revoked", "MemberInvitation", invitation.Id, new { email = invitation.Email }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Changes a member's role; it applies from their next request.</summary>
    /// <param name="userId">The member.</param>
    /// <param name="request">The new role.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400; 403; 404; 409 own-role or last-owner.</returns>
    [HttpPut("{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<MemberProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ParseRole(request.Role) is not { } newRole)
        {
            ModelState.AddModelError("role", "role-invalid");
            return ValidationProblem(ModelState);
        }

        var tenantId = User.TenantId()!.Value;
        var myRole = await RoleAsync(cancellationToken);
        var membership = await context.Memberships.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken);

        if (membership is not null && membership.Role != TenantRole.Owner && await context.ExternalIdentities.AnyAsync(row => row.TenantId == tenantId && row.UserId == userId && row.Active, cancellationToken))
        {
            return Conflict(new MemberProblem("managed-externally"));
        }

        if (membership is null)
        {
            return myRole is TenantRole.Owner or TenantRole.Admin ? NotFound() : Forbid();
        }

        if (!MayManage(myRole, membership.Role) || !MayManage(myRole, newRole))
        {
            return Forbid();
        }

        if (userId == User.UserId())
        {
            return Conflict(new MemberProblem(membership.Role == TenantRole.Owner && await LastOwnerAsync(tenantId, cancellationToken) ? "last-owner" : "own-role"));
        }

        if (membership.Role == TenantRole.Owner && newRole != TenantRole.Owner && await LastOwnerAsync(tenantId, cancellationToken))
        {
            return Conflict(new MemberProblem("last-owner"));
        }

        await context.Memberships.Where(row => row.Id == membership.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Role, newRole), cancellationToken);
        await AuditAsync(tenantId, "member.role-changed", "Membership", membership.Id, new { user = userId, from = Name(membership.Role), to = Name(newRole) }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Takes a member out of the organisation, or leaves it oneself. Their sessions end at once.</summary>
    /// <param name="userId">The member; one's own id to leave.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404; 409 last-owner.</returns>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<MemberProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId()!.Value;
        var myRole = await RoleAsync(cancellationToken);
        var leaving = userId == User.UserId();
        var membership = await context.Memberships.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken);

        if (membership is not null && membership.Role != TenantRole.Owner && await context.ExternalIdentities.AnyAsync(row => row.TenantId == tenantId && row.UserId == userId && row.Active, cancellationToken))
        {
            return Conflict(new MemberProblem("managed-externally"));
        }

        if (membership is null)
        {
            return myRole is TenantRole.Owner or TenantRole.Admin ? NotFound() : Forbid();
        }

        if (!leaving && !MayManage(myRole, membership.Role))
        {
            return Forbid();
        }

        if (membership.Role == TenantRole.Owner && await LastOwnerAsync(tenantId, cancellationToken))
        {
            return Conflict(new MemberProblem("last-owner"));
        }

        await context.Memberships.Where(row => row.Id == membership.Id).ExecuteDeleteAsync(cancellationToken);
        await AuditAsync(tenantId, leaving ? "member.left" : "member.removed", "Membership", membership.Id, new { user = userId, role = Name(membership.Role) }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());

        if (user is not null)
        {
            // Sessions carry the organisation: a new stamp ends them, so nobody keeps working in it.
            await users.UpdateSecurityStampAsync(user);

            if (leaving)
            {
                await sessions.SignInAsync(user, preferredTenant: null, cancellationToken);
            }
        }

        return NoContent();
    }

    private static string Link(string baseAddress, string token) => $"{baseAddress.TrimEnd('/')}/join/{token}";

    private static string Name(TenantRole role) => role.ToString().ToLowerInvariant();

    private static TenantRole? ParseRole(string? role) =>
        Enum.TryParse<TenantRole>(role, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(role, out _) ? parsed : null;

    /// <summary>Owners manage everyone; admins everyone but owners (owner decision, W18).</summary>
    private static bool MayManage(TenantRole? me, TenantRole other) =>
        me == TenantRole.Owner || (me == TenantRole.Admin && other != TenantRole.Owner);

    private async Task<string> BaseAddressAsync(CancellationToken cancellationToken) =>
        (await instance.FindAsync(cancellationToken))?.BaseAddress ?? $"{Request.Scheme}://{Request.Host}";

    private Task<bool> LastOwnerAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Memberships.CountAsync(row => row.TenantId == tenantId && row.Role == TenantRole.Owner, cancellationToken).ContinueWith(task => task.Result <= 1, TaskScheduler.Default);

    private async Task<TenantRole?> RoleAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();

        return tenantId is null
            ? null
            : await context.Memberships.Where(row => row.TenantId == tenantId && row.UserId == userId).Select(row => (TenantRole?)row.Role).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<(MemberInvitation? Invitation, IActionResult? Refused)> ManagedInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var role = await RoleAsync(cancellationToken);

        if (role is not (TenantRole.Owner or TenantRole.Admin))
        {
            return (null, Forbid());
        }

        var invitation = await context.MemberInvitations.FirstOrDefaultAsync(row => row.Id == invitationId && row.TenantId == User.TenantId(), cancellationToken);

        return invitation is null ? (null, NotFound()) : !MayManage(role, invitation.Role) ? (null, Forbid()) : (invitation, null);
    }

    private Task AuditAsync(Guid tenantId, string action, string entityType, Guid entityId, object diff, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, User.UserId().ToString()!, action, entityType, entityId.ToString(), JsonSerializer.Serialize(diff), trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>Why a member change was refused.</summary>
/// <param name="Code">last-owner, own-role, email-not-set-up, account-exists or invitation-invalid.</param>
public sealed record MemberProblem(string Code);

/// <summary>The Members page (W18 A).</summary>
/// <param name="Members">Owners first, then by role and name.</param>
/// <param name="Invitations">Open and expired invitations, newest first.</param>
/// <param name="CanManageOwners">Whether the signed-in user is an owner.</param>
/// <param name="EmailReady">Whether invitations go out by email; otherwise links are copied.</param>
public sealed record MembersResponse(IReadOnlyList<MemberItem> Members, IReadOnlyList<InvitationItem> Invitations, bool CanManageOwners, bool EmailReady);

/// <summary>A member.</summary>
/// <param name="Id">The user.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Address.</param>
/// <param name="Role">owner, admin, editor, analyst or viewer.</param>
/// <param name="TwoFactor">App code or passkey set up.</param>
/// <param name="LastSignInAt">When they last signed in (UTC).</param>
/// <param name="You">The signed-in user.</param>
/// <param name="ManagedBy">The host system that manages this member (W22), or null; such members are locked here.</param>
public sealed record MemberItem(Guid Id, string Name, string Email, string Role, bool TwoFactor, DateTimeOffset? LastSignInAt, bool You, string? ManagedBy = null);

/// <summary>An invitation.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Email">The address.</param>
/// <param name="Role">The role it gives.</param>
/// <param name="InvitedBy">Who invited.</param>
/// <param name="InvitedAt">When (UTC).</param>
/// <param name="ExpiresAt">Until when the link works (UTC).</param>
/// <param name="SentAt">When the email went out; null for link only.</param>
/// <param name="Open">Whether the link still works.</param>
public sealed record InvitationItem(Guid Id, string Email, string Role, string InvitedBy, DateTimeOffset InvitedAt, DateTimeOffset ExpiresAt, DateTimeOffset? SentAt, bool Open);

/// <summary>New invitations.</summary>
/// <param name="Emails">Addresses, as typed.</param>
/// <param name="Role">The role for all of them.</param>
public sealed record InviteMembersRequest(IReadOnlyList<string>? Emails, string? Role);

/// <summary>What inviting did.</summary>
/// <param name="Invitations">The new invitations with their links.</param>
/// <param name="AlreadyMembers">Addresses that are members already; not invited.</param>
/// <param name="Sent">Whether every email went out.</param>
public sealed record InvitedResponse(IReadOnlyList<CreatedInvitation> Invitations, IReadOnlyList<string> AlreadyMembers, bool Sent);

/// <summary>A new invitation.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Email">The address.</param>
/// <param name="Link">The link to join.</param>
public sealed record CreatedInvitation(Guid Id, string Email, string Link);

/// <summary>An invitation's link.</summary>
/// <param name="Link">The link to join.</param>
public sealed record InvitationLinkResponse(string Link);

/// <summary>A new role.</summary>
/// <param name="Role">owner, admin, editor, analyst or viewer.</param>
public sealed record ChangeRoleRequest(string? Role);
