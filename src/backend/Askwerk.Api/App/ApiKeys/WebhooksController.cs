// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.App.Jobs;
using Askwerk.Api.Identity;
using Askwerk.Api.Integrations;
using Askwerk.Application.Persistence;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.ApiKeys;

/// <summary>
/// Organisation → API → Webhooks (AWK-32, C32): the same webhooks as the Public API, for owners and admins, with a
/// ping to try one.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/webhooks")]
public sealed class WebhooksController(AskwerkDbContext context, WebhookService webhooks, IUnitOfWork unitOfWork, JobRunner jobs) : ControllerBase
{
    private WebhookActor Actor => new(AuditActorKind.User, User.UserId().ToString()!);

    /// <summary>The organisation's webhooks with their last delivery.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The webhooks; 403 role-not-allowed.</returns>
    [HttpGet]
    [ProducesResponseType<WebhookList>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        await RefusedAsync(cancellationToken) ?? Ok(new WebhookList(await webhooks.ListAsync(cancellationToken)));

    /// <summary>Creates a webhook; the secret is in this answer only.</summary>
    /// <param name="request">URL, events, description.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the secret; 400 url-invalid or events-invalid; 403; 409 webhooks-too-many.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedWebhook>(StatusCodes.Status201Created)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(WebhookRequest request, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var (webhook, secret, refusal) = await webhooks.CreateAsync(User.TenantId()!.Value, request, Actor, cancellationToken);

        if (refusal is not null)
        {
            return StatusCode(refusal.Status, new WebhookProblem(refusal.Code));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CreatedWebhook(webhook!, secret!));
    }

    /// <summary>Changes a webhook; active true resumes a paused one.</summary>
    /// <param name="id">The webhook.</param>
    /// <param name="request">What changes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The webhook; 400; 403; 404.</returns>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<WebhookView>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Change(Guid id, WebhookChange request, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var (webhook, refusal) = await webhooks.ChangeAsync(id, request, Actor, cancellationToken);

        if (refusal is not null)
        {
            return StatusCode(refusal.Status, new WebhookProblem(refusal.Code));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(webhook);
    }

    /// <summary>Deletes a webhook.</summary>
    /// <param name="id">The webhook.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await webhooks.DeleteAsync(id, Actor, cancellationToken) is { } refusal)
        {
            return StatusCode(refusal.Status, new WebhookProblem(refusal.Code));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>A webhook's deliveries, newest first.</summary>
    /// <param name="id">The webhook.</param>
    /// <param name="cursor">From the previous page.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page; 403; 404.</returns>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType<DeliveryPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deliveries(Guid id, Guid? cursor, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        return await webhooks.DeliveriesAsync(id, cursor, 50, cancellationToken) is { } page ? Ok(page) : NotFound(new WebhookProblem("webhook-not-found"));
    }

    /// <summary>Sends a delivery's event again.</summary>
    /// <param name="id">The webhook.</param>
    /// <param name="deliveryId">The delivery.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202; 403; 404.</returns>
    [HttpPost("{id:guid}/deliveries/{deliveryId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Replay(Guid id, Guid deliveryId, CancellationToken cancellationToken) =>
        QueueAsync(() => webhooks.ReplayAsync(id, deliveryId, cancellationToken), cancellationToken);

    /// <summary>Sends a ping event to try the webhook.</summary>
    /// <param name="id">The webhook.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202; 403; 404.</returns>
    [HttpPost("{id:guid}/ping")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<WebhookProblem>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Ping(Guid id, CancellationToken cancellationToken) =>
        QueueAsync(() => webhooks.PingAsync(id, cancellationToken), cancellationToken);

    private async Task<IActionResult> QueueAsync(Func<Task<WebhookRefusal?>> work, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await work() is { } refusal)
        {
            return StatusCode(refusal.Status, new WebhookProblem(refusal.Code));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        jobs.Nudge();

        return Accepted();
    }

    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is TenantRole.Owner or TenantRole.Admin ? null : StatusCode(StatusCodes.Status403Forbidden, new WebhookProblem("role-not-allowed"));
    }
}

/// <summary>Why a webhook change was refused.</summary>
/// <param name="Code">role-not-allowed, url-invalid, events-invalid, webhooks-too-many, webhook-not-found or delivery-not-found.</param>
public sealed record WebhookProblem(string Code);
