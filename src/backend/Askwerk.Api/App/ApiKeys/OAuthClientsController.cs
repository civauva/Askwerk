// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.ApiKeys;

/// <summary>
/// Organisation → API → OAuth clients (AWK-32, C32 B): owners and admins create a client with the key presets and
/// see its secret once, and revoke it; revoking ends its tokens at once. Every change is audited.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/oauth-clients")]
public sealed class OAuthClientsController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly int[] Expiries = [30, 90, 365];

    /// <summary>The organisation's clients that are not revoked, newest first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The clients; 403 for other roles.</returns>
    [HttpGet]
    [ProducesResponseType<OAuthClientList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiKeyProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        await RefusedAsync(cancellationToken)
        ?? Ok(new OAuthClientList([.. (await context.OAuthClients.Where(client => client.RevokedAt == null).OrderByDescending(client => client.CreatedAt).ToListAsync(cancellationToken)).Select(OAuthClientItem.Of)]));

    /// <summary>Creates a client; its secret is in this answer only.</summary>
    /// <param name="request">Name, scopes and expiry, as for keys.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the client and its secret; 400 at the field; 403 for other roles.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedOAuthClient>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiKeyProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpiresInDays is { } days && !Expiries.Contains(days))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["expiresInDays"] = ["expiry-not-offered"] }));
        }

        var now = time.GetUtcNow();
        var (client, secret) = OAuthClient.Issue(Guid.CreateVersion7(now), User.TenantId()!.Value, request.Name ?? "", request.Scopes ?? [], request.ExpiresInDays is { } chosen ? now.AddDays(chosen) : null, User.UserId()!.Value, now);
        context.OAuthClients.Add(client);
        await AuditAsync(client, "oauth-client.created", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CreatedOAuthClient(OAuthClientItem.Of(client), secret));
    }

    /// <summary>Revokes a client: its tokens stop working at once.</summary>
    /// <param name="id">The client.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles; 404.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiKeyProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.OAuthClients.FirstOrDefaultAsync(client => client.Id == id && client.RevokedAt == null, cancellationToken) is not { } client)
        {
            return NotFound();
        }

        client.Revoke(time.GetUtcNow());
        await AuditAsync(client, "oauth-client.revoked", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private Task AuditAsync(OAuthClient client, string action, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), client.TenantId, AuditActorKind.User, User.UserId().ToString(), action, "OAuthClient", client.Id.ToString(), $"{client.Name}: {string.Join(' ', client.Scopes)}", trace.CurrentTraceId),
            cancellationToken);

    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is TenantRole.Owner or TenantRole.Admin
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new ApiKeyProblem("role-not-allowed", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
    }
}

/// <summary>A client as the list shows it; never with its secret.</summary>
/// <param name="Id">Its id; revoke with it.</param>
/// <param name="Name">What the organisation calls it.</param>
/// <param name="ClientId">The public client id (cl_…).</param>
/// <param name="Scopes">What its tokens may do.</param>
/// <param name="CreatedAt">Created (UTC).</param>
/// <param name="LastUsedAt">Last token (UTC).</param>
/// <param name="ExpiresAt">When it stops working (UTC); never when empty.</param>
public sealed record OAuthClientItem(Guid Id, string Name, string ClientId, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt)
{
    /// <summary>The item of a client.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The item.</returns>
    public static OAuthClientItem Of(OAuthClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new OAuthClientItem(client.Id, client.Name, client.ClientId, client.Scopes, client.CreatedAt, client.LastUsedAt, client.ExpiresAt);
    }
}

/// <summary>The organisation's clients.</summary>
/// <param name="Items">Newest first.</param>
public sealed record OAuthClientList(IReadOnlyList<OAuthClientItem> Items);

/// <summary>A client just created, with its secret.</summary>
/// <param name="Client">The client.</param>
/// <param name="ClientSecret">The secret (acs_…); shown this once.</param>
public sealed record CreatedOAuthClient(OAuthClientItem Client, string ClientSecret);
