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
/// Settings → API (AWK-25, W14 C and D): the organisation's keys for the Public API. Owners and admins create a key,
/// see its secret once, and revoke it; every change is audited.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/api-keys")]
public sealed class ApiKeysController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Managers = [TenantRole.Owner, TenantRole.Admin];

    /// <summary>The expiry choices of the dialog, in days; none means never (W14 D).</summary>
    private static readonly int[] Expiries = [30, 90, 365];

    /// <summary>The organisation's keys that are not revoked, newest first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The keys, without their secrets; 403 for other roles.</returns>
    [HttpGet]
    [ProducesResponseType<ApiKeyList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiKeyProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        await RefusedAsync(cancellationToken)
        ?? Ok(new ApiKeyList([.. (await context.ApiKeys.Where(key => key.RevokedAt == null).OrderByDescending(key => key.CreatedAt).ThenByDescending(key => key.Id).ToListAsync(cancellationToken)).Select(ApiKeyItem.Of)]));

    /// <summary>Creates a key; its secret is in this answer only.</summary>
    /// <param name="request">Name, scopes and expiry.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the key and its secret; 400 at the field; 403 for other roles.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedApiKey>(StatusCodes.Status201Created)]
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
        var (key, secret) = ApiKey.Issue(Guid.CreateVersion7(now), User.TenantId()!.Value, request.Name ?? "", request.Scopes ?? [], request.ExpiresInDays is { } chosen ? now.AddDays(chosen) : null, User.UserId()!.Value, now);
        context.ApiKeys.Add(key);
        await AuditAsync(key, "api-key.created", string.Join(' ', key.Scopes), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CreatedApiKey(ApiKeyItem.Of(key), secret));
    }

    /// <summary>
    /// A trial key for trying the public API in Developers (AWK-34, W27): read-only (surveys and responses) and ending
    /// after one hour. Its secret is in this answer only.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the key and its secret; 403 for roles other than owner and admin.</returns>
    [HttpPost("trial")]
    [ProducesResponseType<CreatedApiKey>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiKeyProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Trial(CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var now = time.GetUtcNow();
        var (key, secret) = ApiKey.Issue(Guid.CreateVersion7(now), User.TenantId()!.Value, "Trial key (Developers, 1 hour)", [ApiScopes.SurveysRead, ApiScopes.ResponsesRead], now.AddHours(1), User.UserId()!.Value, now);
        context.ApiKeys.Add(key);
        await AuditAsync(key, "api-key.created", string.Join(' ', key.Scopes), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CreatedApiKey(ApiKeyItem.Of(key), secret));
    }

    /// <summary>Revokes a key: calls with it fail at once. The audit log keeps it.</summary>
    /// <param name="keyId">The key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles; 404 when it is not this organisation's.</returns>
    [HttpDelete("{keyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiKeyProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid keyId, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.ApiKeys.FirstOrDefaultAsync(key => key.Id == keyId && key.RevokedAt == null, cancellationToken) is not { } key)
        {
            return NotFound();
        }

        key.Revoke(time.GetUtcNow());
        await AuditAsync(key, "api-key.revoked", null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private Task AuditAsync(ApiKey key, string action, string? details, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), key.TenantId, AuditActorKind.User, User.UserId().ToString(), action, "ApiKey", key.Id.ToString(), details is null ? key.Name : $"{key.Name}: {details}", trace.CurrentTraceId),
            cancellationToken);

    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Managers.Contains(known)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new ApiKeyProblem("role-not-allowed", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
    }
}

/// <summary>A new key.</summary>
/// <param name="Name">What the organisation calls it.</param>
/// <param name="Scopes">What it may do: a preset's scopes or some of them.</param>
/// <param name="ExpiresInDays">30, 90 or 365; never when empty.</param>
public sealed record CreateApiKeyRequest(string? Name, IReadOnlyList<string>? Scopes, int? ExpiresInDays);

/// <summary>A key as the list shows it.</summary>
/// <param name="Id">The key.</param>
/// <param name="Name">What the organisation calls it.</param>
/// <param name="Scopes">What it may do.</param>
/// <param name="Shown">Its first eight characters.</param>
/// <param name="CreatedAt">Created (UTC).</param>
/// <param name="LastUsedAt">Last call (UTC).</param>
/// <param name="ExpiresAt">When it stops working (UTC); never when empty.</param>
public sealed record ApiKeyItem(Guid Id, string Name, IReadOnlyList<string> Scopes, string Shown, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt)
{
    /// <summary>The item of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The item.</returns>
    public static ApiKeyItem Of(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new ApiKeyItem(key.Id, key.Name, key.Scopes, key.Shown, key.CreatedAt, key.LastUsedAt, key.ExpiresAt);
    }
}

/// <summary>The organisation's keys.</summary>
/// <param name="Items">Newest first.</param>
public sealed record ApiKeyList(IReadOnlyList<ApiKeyItem> Items);

/// <summary>A key just created, with its secret.</summary>
/// <param name="Key">The key.</param>
/// <param name="Secret">The whole key; shown this once.</param>
public sealed record CreatedApiKey(ApiKeyItem Key, string Secret);

/// <summary>Why a key request was refused.</summary>
/// <param name="Code">role-not-allowed.</param>
/// <param name="TraceId">The request's trace.</param>
public sealed record ApiKeyProblem(string Code, string? TraceId);
