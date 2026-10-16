// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
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
/// Joining an organisation from an invitation link (AWK-30, W18 C). The link works once, for 7 days. A new person
/// sets name and password; someone with an account joins while signed in. Either way the session then works in the
/// organisation, with the two-factor setup first for owners and admins.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[AllowAnonymous]
[Route("api/app/invitations/{token}")]
public sealed class JoinController(
    AskwerkDbContext context,
    DbSessionContext session,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace,
    IAccounts accounts,
    UserManager<User> users,
    SessionService sessions) : ControllerBase
{
    /// <summary>What the link offers: the organisation, the role, who invited, and whether the address has an account.</summary>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The invitation; 410 when the link no longer works.</returns>
    [HttpGet]
    [ProducesResponseType<JoinInvitationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MemberProblem>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Show(string token, CancellationToken cancellationToken)
    {
        var invitation = await OpenInvitationAsync(token, cancellationToken);

        if (invitation is null)
        {
            return Gone();
        }

        var organisation = await context.Tenants.Where(tenant => tenant.Id == invitation.TenantId).Select(tenant => tenant.Name).FirstAsync(cancellationToken);
        var invitedBy = await context.Users.Where(user => user.Id == invitation.InvitedBy).Select(user => user.DisplayName).FirstOrDefaultAsync(cancellationToken);
        var exists = await users.FindByEmailAsync(invitation.Email) is not null;
        var signedIn = User.UserId() is { } userId ? await context.Users.Where(user => user.Id == userId).Select(user => user.Email).FirstOrDefaultAsync(cancellationToken) : null;

        return Ok(new JoinInvitationResponse(organisation, invitation.Role.ToString().ToLowerInvariant(), invitedBy ?? "", invitation.Email, exists, signedIn, invitation.ExpiresAt));
    }

    /// <summary>Joins: signed in, with the current account; otherwise with a new account for the invited address.</summary>
    /// <param name="token">The link's token.</param>
    /// <param name="request">Name and password for a new account; empty when signed in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 400 at name or password; 409 account-exists; 410 when the link no longer works.</returns>
    [HttpPost("accept")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<MemberProblem>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<MemberProblem>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Accept(string token, JoinRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invitation = await OpenInvitationAsync(token, cancellationToken);

        if (invitation is null)
        {
            return Gone();
        }

        var now = time.GetUtcNow();
        User? user;

        if (User.UserId() is { } signedIn)
        {
            user = await users.FindByIdAsync(signedIn.ToString());
        }
        else
        {
            if (await users.FindByEmailAsync(invitation.Email) is not null)
            {
                return Conflict(new MemberProblem("account-exists"));
            }

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            {
                ModelState.AddModelError("name", string.IsNullOrWhiteSpace(request.Name) ? "name-required" : "name-too-long");
                return ValidationProblem(ModelState);
            }

            user = Domain.Identity.User.Create(Guid.CreateVersion7(now), invitation.Email, request.Name.Trim(), now);
            session.UserId = user.Id;

            if (await accounts.CreateAsync(user, request.Password ?? "", cancellationToken) is { } passwordError)
            {
                ModelState.AddModelError("password", passwordError);
                return ValidationProblem(ModelState);
            }

            await AuditAsync(null, user.Id, "user.created", "User", user.Id, null, cancellationToken);
        }

        if (user is null)
        {
            return Unauthorized();
        }

        if (!await context.Memberships.AnyAsync(row => row.TenantId == invitation.TenantId && row.UserId == user.Id, cancellationToken))
        {
            var membership = new Membership(Guid.CreateVersion7(now), user.Id, invitation.TenantId, null, invitation.Role, now);
            context.Memberships.Add(membership);
            await AuditAsync(invitation.TenantId, user.Id, "member.joined", "Membership", membership.Id, new { role = invitation.Role.ToString().ToLowerInvariant(), email = invitation.Email }, cancellationToken);
        }

        context.MemberInvitations.Remove(invitation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(await sessions.SignInAsync(user, invitation.TenantId, cancellationToken));
    }

    private ObjectResult Gone() => StatusCode(StatusCodes.Status410Gone, new MemberProblem("invitation-invalid"));

    /// <summary>
    /// The open invitation of a token. Nobody is in an organisation yet, so a definer function finds its organisation
    /// by the token's hash; the row itself is then read inside that organisation.
    /// </summary>
    private async Task<MemberInvitation?> OpenInvitationAsync(string token, CancellationToken cancellationToken)
    {
        var hash = MemberInvitation.HashOf(token);
        var tenantId = await context.Database.SqlQuery<Guid?>($"SELECT member_invitation_tenant({hash}) AS \"Value\"").FirstOrDefaultAsync(cancellationToken);

        if (tenantId is null)
        {
            return null;
        }

        session.TenantId = tenantId;
        await context.Database.CloseConnectionAsync();
        var invitation = await context.MemberInvitations.FirstOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);

        return invitation is not null && invitation.IsOpen(time.GetUtcNow()) ? invitation : null;
    }

    private Task AuditAsync(Guid? tenantId, Guid actor, string action, string entityType, Guid entityId, object? diff, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, actor.ToString(), action, entityType, entityId.ToString(), diff is null ? null : JsonSerializer.Serialize(diff), trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>What an invitation link offers (W18 C).</summary>
/// <param name="Organisation">The organisation's name.</param>
/// <param name="Role">The role it gives.</param>
/// <param name="InvitedBy">Who invited.</param>
/// <param name="Email">The invited address.</param>
/// <param name="AccountExists">Whether the address has an account: then the person signs in to join.</param>
/// <param name="SignedInAs">The address of the account signed in in this browser, if any.</param>
/// <param name="ExpiresAt">Until when the link works (UTC).</param>
public sealed record JoinInvitationResponse(string Organisation, string Role, string InvitedBy, string Email, bool AccountExists, string? SignedInAs, DateTimeOffset ExpiresAt);

/// <summary>Joining.</summary>
/// <param name="Name">For a new account: the display name.</param>
/// <param name="Password">For a new account: at least 12 characters.</param>
public sealed record JoinRequest(string? Name = null, string? Password = null);
