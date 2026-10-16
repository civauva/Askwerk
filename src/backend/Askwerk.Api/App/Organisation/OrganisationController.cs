// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Organisation;

/// <summary>
/// Administration → Organisation (AWK-38, W29): the organisation chosen in the session. Every member reads it; owners
/// and admins rename it and change its short name; owners hand ownership on and delete it.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/organisation")]
public sealed class OrganisationController(AskwerkDbContext context, ITenantRepository tenants, IInstanceSettingsRepository instance, TenantStates states, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    /// <summary>The organisation: name, short name, size, and the installation's time zone.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The organisation; 409 organisation-not-chosen.</returns>
    [HttpGet]
    [ProducesResponseType<OrganisationDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<OrganisationProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (await TenantAsync(cancellationToken) is not { } tenant)
        {
            return Conflict(new OrganisationProblem("organisation-not-chosen"));
        }

        var role = await RoleAsync(tenant.Id, cancellationToken);

        return Ok(new OrganisationDetails(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.CreatedAt,
            await context.Memberships.CountAsync(membership => membership.TenantId == tenant.Id, cancellationToken),
            await context.Surveys.CountAsync(survey => survey.DeletedAt == null, cancellationToken),
            role?.ToString().ToLowerInvariant() ?? "viewer",
            (await instance.FindAsync(cancellationToken))?.TimeZone ?? "UTC"));
    }

    /// <summary>Renames the organisation or changes its short name; owners and admins.</summary>
    /// <param name="request">Name and short name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at the field (name-required, slug-invalid, slug-taken); 403 for other roles.</returns>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Change(ChangeOrganisationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenant)
        {
            return Conflict(new OrganisationProblem("organisation-not-chosen"));
        }

        if (await RoleAsync(tenant.Id, cancellationToken) is not (TenantRole.Owner or TenantRole.Admin))
        {
            return Forbid();
        }

        var slug = (request.Slug ?? tenant.Slug).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError("name", "name-required");
        }
        else if (!Tenant.IsValidSlug(slug))
        {
            ModelState.AddModelError("slug", "slug-invalid");
        }
        else if (slug != tenant.Slug && await tenants.SlugTakenAsync(slug, cancellationToken))
        {
            ModelState.AddModelError("slug", "slug-taken");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        tenant.Rename(request.Name!);
        tenant.ChangeSlug(slug);
        await AuditAsync(tenant.Id, "organisation.changed", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Makes another member the owner; the current owner becomes an admin. Owners only.</summary>
    /// <param name="request">The new owner.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles; 404 when the person is not a member.</returns>
    [HttpPost("transfer")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Transfer(TransferOwnershipRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenant)
        {
            return Conflict(new OrganisationProblem("organisation-not-chosen"));
        }

        var me = User.UserId();

        if (await RoleAsync(tenant.Id, cancellationToken) != TenantRole.Owner)
        {
            return Forbid();
        }

        if (request.UserId == me || !await context.Memberships.AnyAsync(membership => membership.TenantId == tenant.Id && membership.UserId == request.UserId, cancellationToken))
        {
            return NotFound();
        }

        await context.Memberships.Where(membership => membership.TenantId == tenant.Id && membership.UserId == request.UserId).ExecuteUpdateAsync(set => set.SetProperty(row => row.Role, TenantRole.Owner), cancellationToken);
        await context.Memberships.Where(membership => membership.TenantId == tenant.Id && membership.UserId == me).ExecuteUpdateAsync(set => set.SetProperty(row => row.Role, TenantRole.Admin), cancellationToken);
        await AuditAsync(tenant.Id, "organisation.ownership-transferred", cancellationToken, new { to = request.UserId });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Deletes the organisation: closed for everyone at once, its data kept until an installation administrator removes it. Owners only.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        if (await TenantAsync(cancellationToken) is not { } tenant)
        {
            return Conflict(new OrganisationProblem("organisation-not-chosen"));
        }

        if (await RoleAsync(tenant.Id, cancellationToken) != TenantRole.Owner)
        {
            return Forbid();
        }

        tenant.Delete(time.GetUtcNow());
        await AuditAsync(tenant.Id, "organisation.deleted", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        states.Changed(tenant.Id);

        return NoContent();
    }

    private async Task<Tenant?> TenantAsync(CancellationToken cancellationToken) =>
        User.TenantId() is { } tenantId ? await context.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken) : null;

    private Task<TenantRole?> RoleAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();

        return context.Memberships.Where(membership => membership.TenantId == tenantId && membership.UserId == userId).Select(membership => (TenantRole?)membership.Role).FirstOrDefaultAsync(cancellationToken);
    }

    private Task AuditAsync(Guid tenantId, string action, CancellationToken cancellationToken, object? diff = null) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, User.UserId().ToString()!, action, "Tenant", tenantId.ToString(), diff is null ? null : System.Text.Json.JsonSerializer.Serialize(diff), trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>The organisation (AWK-38).</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Slug">Its short name, used in sign-in links.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="Members">How many members it has.</param>
/// <param name="Surveys">How many surveys it has.</param>
/// <param name="Role">The signed-in user's role.</param>
/// <param name="TimeZone">The installation's time zone, which its dates use.</param>
public sealed record OrganisationDetails(Guid Id, string Name, string Slug, DateTimeOffset CreatedAt, int Members, int Surveys, string Role, string TimeZone);

/// <summary>A new name or short name.</summary>
/// <param name="Name">The name.</param>
/// <param name="Slug">The short name; unchanged when not given.</param>
public sealed record ChangeOrganisationRequest(string? Name, string? Slug);

/// <summary>The member who becomes the owner.</summary>
/// <param name="UserId">Their user id.</param>
public sealed record TransferOwnershipRequest(Guid UserId);

/// <summary>Why a request about the organisation was refused.</summary>
/// <param name="Code">Stable code.</param>
public sealed record OrganisationProblem(string Code);
