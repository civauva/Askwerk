// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Identity;
using Askwerk.Application.Persistence;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.Integrations;

/// <summary>
/// Public API: provisioning (AWK-12, W22), scope users:manage. The host system is the master of its users: it creates,
/// changes and deactivates them by its own id; on the Members page they are locked. Needs Provisioning switched on in
/// Organisation → Sign-in.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Authorize(Policy = PublicApi.UsersManage)]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class PublicUsersController(AskwerkDbContext context, ExternalUsers externalUsers, IUnitOfWork unitOfWork) : ControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    /// <summary>Lists the provisioned users, oldest first.</summary>
    /// <remarks>Scope users:manage. Pages on a cursor: pass nextCursor as cursor until it is null. Deactivated users are listed with active false.</remarks>
    /// <param name="cursor">Where the page starts, from the previous page's nextCursor.</param>
    /// <param name="limit">At most 500; 100 when not given.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of users.</returns>
    /// <response code="200">A page of users.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks users:manage.</response>
    /// <response code="409">provisioning-off.</response>
    [HttpGet]
    [ProducesResponseType<PublicUserPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> List(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (await OffAsync(cancellationToken) is { } off)
        {
            return off;
        }

        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var after = Guid.TryParse(cursor, out var parsed) ? parsed : Guid.Empty;
        var identities = await context.ExternalIdentities.AsNoTracking()
            .Where(row => row.Source == "provisioning" && row.Id.CompareTo(after) > 0)
            .OrderBy(row => row.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);
        var page = identities.Take(size).ToList();
        var items = new List<PublicUser>();

        foreach (var identity in page)
        {
            items.Add(await ViewAsync(identity, cancellationToken));
        }

        return Ok(new PublicUserPage(items, identities.Count > size ? page[^1].Id.ToString() : null));
    }

    /// <summary>Creates or changes a user by the host's id.</summary>
    /// <remarks>
    /// Scope users:manage. Creates the user (without an Askwerk password) when the id is new, matching an existing
    /// account by email once; otherwise updates email, name and role. active false deactivates, true activates again.
    /// Owners are never provisioned; an owner's role is never changed.
    /// </remarks>
    /// <param name="externalId">The host system's user id.</param>
    /// <param name="request">Email, name, role and whether active.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The user.</returns>
    /// <response code="200">Changed.</response>
    /// <response code="201">Created.</response>
    /// <response code="400">user-invalid or owner-not-provisioned.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks users:manage.</response>
    /// <response code="409">provisioning-off.</response>
    [HttpPut("{externalId}")]
    [ProducesResponseType<PublicUser>(StatusCodes.Status200OK)]
    [ProducesResponseType<PublicUser>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Put(string externalId, PublicUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await OffAsync(cancellationToken) is { } off)
        {
            return off;
        }

        if (!Enum.TryParse<TenantRole>(request.Role, ignoreCase: true, out var role) || request.Role is null || int.TryParse(request.Role, out _))
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status400BadRequest, "user-invalid");
        }

        if (role == TenantRole.Owner)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status400BadRequest, "owner-not-provisioned");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name) || externalId.Length > 200)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status400BadRequest, "user-invalid");
        }

        var tenantId = User.TenantId()!.Value;
        ExternalUserResult result;

        try
        {
            result = await externalUsers.ApplyAsync(tenantId, externalId, request.Email, request.Name, role, "provisioning", User.IntegrationActor(), cancellationToken);
        }
        catch (DomainValidationException)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status400BadRequest, "user-invalid");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.Active == false)
        {
            await externalUsers.DeactivateAsync(result.Identity, User.IntegrationActor(), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var view = await ViewAsync(result.Identity, cancellationToken);

        return result.Created ? Created($"/api/v1/users/{Uri.EscapeDataString(externalId)}", view) : Ok(view);
    }

    /// <summary>Deactivates a user: out of the organisation, sessions ended; never deleted, their responses stay.</summary>
    /// <remarks>Scope users:manage. PUT with active true lets them in again.</remarks>
    /// <param name="externalId">The host system's user id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Deactivated.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks users:manage.</response>
    /// <response code="404">user-not-found.</response>
    /// <response code="409">provisioning-off.</response>
    [HttpDelete("{externalId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(string externalId, CancellationToken cancellationToken)
    {
        if (await OffAsync(cancellationToken) is { } off)
        {
            return off;
        }

        if (await context.ExternalIdentities.FirstOrDefaultAsync(row => row.ExternalId == externalId, cancellationToken) is not { } identity)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "user-not-found");
        }

        await externalUsers.DeactivateAsync(identity, User.IntegrationActor(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult?> OffAsync(CancellationToken cancellationToken) =>
        await context.TenantSignIns.AnyAsync(row => row.ProvisioningEnabled, cancellationToken) ? null : PublicApi.Problem(HttpContext, StatusCodes.Status409Conflict, "provisioning-off");

    private async Task<PublicUser> ViewAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().FirstAsync(row => row.Id == identity.UserId, cancellationToken);
        var role = await context.Memberships.AsNoTracking().Where(row => row.TenantId == identity.TenantId && row.UserId == identity.UserId).Select(row => (TenantRole?)row.Role).FirstOrDefaultAsync(cancellationToken);

        return new PublicUser(identity.ExternalId, user.Email, user.DisplayName, role?.ToString().ToLowerInvariant(), identity.Active, identity.CreatedAt, identity.UpdatedAt);
    }
}

/// <summary>A user to create or change.</summary>
/// <param name="Email">Their email address.</param>
/// <param name="Name">Their name.</param>
/// <param name="Role">admin, editor, analyst or viewer.</param>
/// <param name="Active">false deactivates; true or empty keeps them in.</param>
public sealed record PublicUserRequest(string? Email, string? Name, string? Role, bool? Active);

/// <summary>A provisioned user.</summary>
/// <param name="ExternalId">The host system's id.</param>
/// <param name="Email">Their email address.</param>
/// <param name="Name">Their name.</param>
/// <param name="Role">Their role; null while deactivated.</param>
/// <param name="Active">Whether they may sign in.</param>
/// <param name="CreatedAt">When provisioned (UTC).</param>
/// <param name="UpdatedAt">When last changed (UTC).</param>
public sealed record PublicUser(string ExternalId, string Email, string Name, string? Role, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A page of users.</summary>
/// <param name="Items">Oldest first.</param>
/// <param name="NextCursor">The cursor of the next page; null on the last.</param>
public sealed record PublicUserPage(IReadOnlyList<PublicUser> Items, string? NextCursor);
