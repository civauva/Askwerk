// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.Integrations;

/// <summary>Who changes a webhook: a user on the API page or an API key.</summary>
/// <param name="Kind">User or API key.</param>
/// <param name="Id">The user's or the key's id.</param>
public sealed record WebhookActor(AuditActorKind Kind, string Id);

/// <summary>Why a webhook change was refused, as a Public API problem code.</summary>
/// <param name="Status">HTTP status.</param>
/// <param name="Code">url-invalid, events-invalid, webhooks-too-many, webhook-not-found or delivery-not-found.</param>
public sealed record WebhookRefusal(int Status, string Code);

/// <summary>
/// Webhooks (AWK-32, C32 A): managing them, raising events into their deliveries (in the same unit of work as the
/// change, so a rolled-back change sends nothing), and the deliveries log. The Public API and the API page share it.
/// </summary>
public sealed class WebhookService(AskwerkDbContext context, ISecretProtector secrets, IAuditWriter audit, TimeProvider time, ITraceContext trace, IHostEnvironment environment)
{
    /// <summary>The organisation's webhooks, newest first, with their last delivery.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The webhooks.</returns>
    public async Task<IReadOnlyList<WebhookView>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Webhooks.AsNoTracking()
            .OrderByDescending(webhook => webhook.CreatedAt)
            .Select(webhook => new { webhook, Last = context.WebhookDeliveries.Where(delivery => delivery.WebhookId == webhook.Id).OrderByDescending(delivery => delivery.Id).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => WebhookView.Of(row.webhook, row.Last))];
    }

    /// <summary>Creates a webhook; the secret is in this answer only.</summary>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="request">URL, events and description.</param>
    /// <param name="actor">Who creates it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The webhook and its secret, or why not.</returns>
    public async Task<(WebhookView? Webhook, string? Secret, WebhookRefusal? Refusal)> CreateAsync(Guid tenantId, WebhookRequest request, WebhookActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Check(request.Url, request.Events, required: true) is { } refusal)
        {
            return (null, null, refusal);
        }

        if (await context.Webhooks.CountAsync(cancellationToken) >= Webhook.MaxPerOrganisation)
        {
            return (null, null, new WebhookRefusal(StatusCodes.Status409Conflict, "webhooks-too-many"));
        }

        var now = time.GetUtcNow();
        var secret = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var webhook = Webhook.Create(Guid.CreateVersion7(now), tenantId, request.Url!.Trim(), request.Events!, request.Description, secrets.Protect(secret), now);
        context.Webhooks.Add(webhook);
        await AuditAsync(webhook, "webhook.created", actor, cancellationToken);

        return (WebhookView.Of(webhook, null), secret, null);
    }

    /// <summary>Changes a webhook's URL, events or state; active true resumes a paused one.</summary>
    /// <param name="webhookId">The webhook.</param>
    /// <param name="request">What changes.</param>
    /// <param name="actor">Who changes it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The webhook, or why not.</returns>
    public async Task<(WebhookView? Webhook, WebhookRefusal? Refusal)> ChangeAsync(Guid webhookId, WebhookChange request, WebhookActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await context.Webhooks.FirstOrDefaultAsync(webhook => webhook.Id == webhookId, cancellationToken) is not { } webhook)
        {
            return (null, new WebhookRefusal(StatusCodes.Status404NotFound, "webhook-not-found"));
        }

        if (Check(request.Url, request.Events, required: false) is { } refusal)
        {
            return (null, refusal);
        }

        webhook.Change(request.Url?.Trim(), request.Events, request.Active);
        await AuditAsync(webhook, "webhook.updated", actor, cancellationToken);

        return (WebhookView.Of(webhook, null), null);
    }

    /// <summary>Deletes a webhook and its deliveries log.</summary>
    /// <param name="webhookId">The webhook.</param>
    /// <param name="actor">Who deletes it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Null, or why not.</returns>
    public async Task<WebhookRefusal?> DeleteAsync(Guid webhookId, WebhookActor actor, CancellationToken cancellationToken)
    {
        if (await context.Webhooks.FirstOrDefaultAsync(webhook => webhook.Id == webhookId, cancellationToken) is not { } webhook)
        {
            return new WebhookRefusal(StatusCodes.Status404NotFound, "webhook-not-found");
        }

        await context.WebhookDeliveries.Where(delivery => delivery.WebhookId == webhookId).ExecuteDeleteAsync(cancellationToken);
        context.Webhooks.Remove(webhook);
        await AuditAsync(webhook, "webhook.deleted", actor, cancellationToken);

        return null;
    }

    /// <summary>A page of a webhook's deliveries, newest first; the cursor is the last id of the page before.</summary>
    /// <param name="webhookId">The webhook.</param>
    /// <param name="cursor">Where to go on.</param>
    /// <param name="limit">At most 100; 50 when not given.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page, or null when the webhook does not exist.</returns>
    public async Task<DeliveryPage?> DeliveriesAsync(Guid webhookId, Guid? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (!await context.Webhooks.AnyAsync(webhook => webhook.Id == webhookId, cancellationToken))
        {
            return null;
        }

        var size = Math.Clamp(limit ?? 50, 1, 100);
        var rows = await context.WebhookDeliveries.AsNoTracking()
            .Where(delivery => delivery.WebhookId == webhookId && (cursor == null || delivery.Id < cursor))
            .OrderByDescending(delivery => delivery.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);

        return new DeliveryPage([.. rows.Take(size).Select(DeliveryView.Of)], rows.Count > size ? rows[size - 1].Id.ToString() : null);
    }

    /// <summary>Sends a delivery's event again, as a new delivery with the same event id.</summary>
    /// <param name="webhookId">The webhook.</param>
    /// <param name="deliveryId">The delivery.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Null, or why not.</returns>
    public async Task<WebhookRefusal?> ReplayAsync(Guid webhookId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var webhook = await context.Webhooks.FirstOrDefaultAsync(each => each.Id == webhookId, cancellationToken);
        var delivery = webhook is null ? null : await context.WebhookDeliveries.AsNoTracking().FirstOrDefaultAsync(each => each.Id == deliveryId && each.WebhookId == webhookId, cancellationToken);

        if (webhook is null || delivery is null)
        {
            return new WebhookRefusal(StatusCodes.Status404NotFound, webhook is null ? "webhook-not-found" : "delivery-not-found");
        }

        Queue(context, webhook, delivery.EventId, delivery.EventType, delivery.Payload, time.GetUtcNow());

        return null;
    }

    /// <summary>Sends a ping event to one webhook, to try it.</summary>
    /// <param name="webhookId">The webhook.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Null, or why not.</returns>
    public async Task<WebhookRefusal?> PingAsync(Guid webhookId, CancellationToken cancellationToken)
    {
        if (await context.Webhooks.FirstOrDefaultAsync(each => each.Id == webhookId, cancellationToken) is not { } webhook)
        {
            return new WebhookRefusal(StatusCodes.Status404NotFound, "webhook-not-found");
        }

        var now = time.GetUtcNow();
        var (eventId, payload) = Event(webhook.TenantId, "ping", new { webhookId }, now);
        Queue(context, webhook, eventId, "ping", payload, now);

        return null;
    }

    /// <summary>
    /// Raises an event: a delivery and a job for every active webhook of the organisation that subscribed to it. Added
    /// to the context, so it is stored with the change that caused it.
    /// </summary>
    /// <param name="context">The database, in the organisation.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="type">The event type.</param>
    /// <param name="data">The event's data.</param>
    /// <param name="now">When it happened.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <param name="surveyId">The survey it is about; webhooks for other surveys do not get it (W36).</param>
    /// <returns>When queued.</returns>
    public static async Task RaiseAsync(AskwerkDbContext context, Guid tenantId, string type, object data, DateTimeOffset now, CancellationToken cancellationToken, Guid? surveyId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var webhooks = (await context.Webhooks.Where(webhook => webhook.TenantId == tenantId && webhook.Active && webhook.Subscribed.Contains(type)).ToListAsync(cancellationToken))
            .Where(webhook => webhook.Covers(surveyId))
            .ToList();

        if (webhooks.Count == 0)
        {
            return;
        }

        var (eventId, payload) = Event(tenantId, type, data, now);

        foreach (var webhook in webhooks)
        {
            Queue(context, webhook, eventId, type, payload, now);
        }
    }

    /// <summary>The signature header: t=timestamp,v1=hex(HMAC-SHA256(secret, "timestamp.body")).</summary>
    /// <param name="secret">The webhook's secret.</param>
    /// <param name="timestamp">Unix seconds.</param>
    /// <param name="body">The JSON body.</param>
    /// <returns>The header value.</returns>
    public static string Signature(string secret, long timestamp, string body) =>
        $"t={timestamp},v1={Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")))}";

    private static (string EventId, string Payload) Event(Guid tenantId, string type, object data, DateTimeOffset now)
    {
        var eventId = $"evt_{Guid.CreateVersion7(now):N}";

        return (eventId, JsonSerializer.Serialize(new { id = eventId, type, occurredAt = now, tenantId, data }, JsonSerializerOptions.Web));
    }

    private static void Queue(AskwerkDbContext context, Webhook webhook, string eventId, string type, string payload, DateTimeOffset now)
    {
        var delivery = WebhookDelivery.Create(Guid.CreateVersion7(now), webhook, eventId, type, payload, now);
        context.WebhookDeliveries.Add(delivery);
        JobRunner.Enqueue(context, webhook.TenantId, DeliverWebhookJob.JobKind, new DeliveryPayload(delivery.Id), now, now);
    }

    private WebhookRefusal? Check(string? url, IReadOnlyList<string>? events, bool required)
    {
        if ((required || url is not null) && !Webhook.IsValidUrl(url?.Trim(), environment.IsDevelopment()))
        {
            return new WebhookRefusal(StatusCodes.Status400BadRequest, "url-invalid");
        }

        if ((required || events is not null) && (events is null || events.Count == 0 || events.Any(each => !Webhook.Events.Contains(each))))
        {
            return new WebhookRefusal(StatusCodes.Status400BadRequest, "events-invalid");
        }

        return null;
    }

    private Task AuditAsync(Webhook webhook, string action, WebhookActor actor, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), webhook.TenantId, actor.Kind, actor.Id, action, "Webhook", webhook.Id.ToString(), JsonSerializer.Serialize(new { url = webhook.Url, events = webhook.Subscribed }), trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>The job's payload: which delivery to try.</summary>
/// <param name="DeliveryId">The delivery.</param>
public sealed record DeliveryPayload(Guid DeliveryId);

/// <summary>
/// Tries one delivery: POSTs the event with its signature and 10 seconds to answer. A 2xx delivers it; otherwise it is
/// tried again after 1 min, 5 min, 30 min, 2 h, 6 h and 12 h, and a webhook failing for 3 days is paused.
/// </summary>
public sealed class DeliverWebhookJob(AskwerkDbContext context, IHttpClientFactory clients, ISecretProtector secrets, TimeProvider time) : IJobHandler
{
    /// <summary>The job kind.</summary>
    public const string JobKind = "webhook.deliver";

    /// <summary>The named HTTP client for deliveries.</summary>
    public const string Client = "webhooks";

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <inheritdoc />
    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var deliveryId = JsonSerializer.Deserialize<DeliveryPayload>(payload, JsonSerializerOptions.Web)!.DeliveryId;
        var delivery = await context.WebhookDeliveries.FirstOrDefaultAsync(each => each.Id == deliveryId, cancellationToken);
        var webhook = delivery is null ? null : await context.Webhooks.FirstOrDefaultAsync(each => each.Id == delivery.WebhookId, cancellationToken);

        if (delivery is null || webhook is null || delivery.Status != WebhookDeliveryStatus.Pending)
        {
            return;
        }

        if (!webhook.Active)
        {
            delivery.Tried(null, "paused", giveUp: true);
            return;
        }

        var now = time.GetUtcNow();
        int? status = null;
        string? error = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url) { Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json") };
            request.Headers.Add("Askwerk-Event", delivery.EventType);
            request.Headers.Add("Askwerk-Delivery", $"dlv_{delivery.Id:N}");
            request.Headers.Add("Askwerk-Signature", WebhookService.Signature(secrets.Unprotect(webhook.SecretProtected), now.ToUnixTimeSeconds(), delivery.Payload));
            using var response = await clients.CreateClient(Client).SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            error = exception.Message;
        }

        delivery.Took((int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds));

        if (status is >= 200 and < 300)
        {
            delivery.Succeeded(status.Value, now);
            webhook.Delivered();
            return;
        }

        var paused = webhook.Failed(now);

        if (delivery.Tried(status, error, paused) is { } wait)
        {
            JobRunner.Enqueue(context, webhook.TenantId, JobKind, new DeliveryPayload(delivery.Id), now + wait, now);
        }
    }
}

/// <summary>A webhook as the API shows it; never with its secret.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Url">Where events go.</param>
/// <param name="Events">Subscribed events.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Active">Whether events are sent.</param>
/// <param name="Paused">Paused for failing 3 days; active true resumes it.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
/// <param name="LastDelivery">The last delivery, if any.</param>
public sealed record WebhookView(Guid Id, string Url, IReadOnlyList<string> Events, string? Description, bool Active, bool Paused, DateTimeOffset CreatedAt, LastDeliveryView? LastDelivery)
{
    internal static WebhookView Of(Webhook webhook, WebhookDelivery? last) =>
        new(webhook.Id, webhook.Url, webhook.Subscribed, webhook.Description, webhook.Active, webhook.PausedAt is not null, webhook.CreatedAt, last is null ? null : new LastDeliveryView(Lower(last.Status), last.DeliveredAt ?? last.CreatedAt, last.ResponseStatus));

    internal static string Lower(WebhookDeliveryStatus status) => status.ToString().ToLowerInvariant();
}

/// <summary>The last delivery of a webhook.</summary>
/// <param name="Status">pending, delivered or failed.</param>
/// <param name="At">When.</param>
/// <param name="ResponseStatus">The last HTTP status answered.</param>
public sealed record LastDeliveryView(string Status, DateTimeOffset At, int? ResponseStatus);

/// <summary>One delivery in the log.</summary>
/// <param name="Id">Its id; replay with it.</param>
/// <param name="Event">The event type.</param>
/// <param name="EventId">The event's id (evt_…).</param>
/// <param name="Status">pending, delivered or failed.</param>
/// <param name="Attempts">Tries made.</param>
/// <param name="ResponseStatus">The last HTTP status answered.</param>
/// <param name="Error">Why the last try got no answer.</param>
/// <param name="CreatedAt">When the event was queued (UTC).</param>
/// <param name="DeliveredAt">When it was delivered.</param>
public sealed record DeliveryView(Guid Id, string Event, string EventId, string Status, int Attempts, int? ResponseStatus, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt)
{
    internal static DeliveryView Of(WebhookDelivery delivery) =>
        new(delivery.Id, delivery.EventType, delivery.EventId, WebhookView.Lower(delivery.Status), delivery.Attempts, delivery.ResponseStatus, delivery.Error, delivery.CreatedAt, delivery.DeliveredAt);
}

/// <summary>A page of deliveries.</summary>
/// <param name="Items">Newest first.</param>
/// <param name="NextCursor">Pass as cursor for the next page; null at the end.</param>
public sealed record DeliveryPage(IReadOnlyList<DeliveryView> Items, string? NextCursor);

/// <summary>The webhooks of the organisation.</summary>
/// <param name="Items">Newest first.</param>
public sealed record WebhookList(IReadOnlyList<WebhookView> Items);

/// <summary>A new webhook.</summary>
/// <param name="Url">An https URL.</param>
/// <param name="Events">export.completed, response.completed, survey.published, survey.closed.</param>
/// <param name="Description">What it is for.</param>
public sealed record WebhookRequest(string? Url, IReadOnlyList<string>? Events, string? Description = null);

/// <summary>A change to a webhook; what is left out stays.</summary>
/// <param name="Url">A new https URL.</param>
/// <param name="Events">New events.</param>
/// <param name="Active">false stops it; true starts it again, also after a pause.</param>
public sealed record WebhookChange(string? Url = null, IReadOnlyList<string>? Events = null, bool? Active = null);

/// <summary>A created webhook with its secret, shown only here.</summary>
/// <param name="Webhook">The webhook.</param>
/// <param name="Secret">Signs every delivery (whsec_…).</param>
public sealed record CreatedWebhook(WebhookView Webhook, string Secret);

/// <summary>
/// Public API: webhooks (AWK-32, C32 A), scope webhooks:manage. Every event is posted signed in Askwerk-Signature;
/// receivers deduplicate by the event id (docs/api/webhooks.md).
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Authorize(Policy = PublicApi.WebhooksManage)]
[Route("api/v1/webhooks")]
[Produces("application/json")]
public sealed class PublicWebhooksController(WebhookService webhooks, IUnitOfWork unitOfWork, JobRunner jobs) : ControllerBase
{
    private WebhookActor Actor => User.IntegrationActor() is var (kind, id) ? new(kind, id) : throw new InvalidOperationException();

    /// <summary>Lists the organisation's webhooks.</summary>
    /// <remarks>Scope webhooks:manage. Newest first, each with its last delivery; never with its secret.</remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The webhooks.</returns>
    /// <response code="200">The webhooks.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks webhooks:manage.</response>
    [HttpGet]
    [ProducesResponseType<WebhookList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<WebhookList> List(CancellationToken cancellationToken) => new(await webhooks.ListAsync(cancellationToken));

    /// <summary>Creates a webhook.</summary>
    /// <remarks>Scope webhooks:manage. The secret is in this answer only; keep it to check Askwerk-Signature. At most 20 webhooks per organisation.</remarks>
    /// <param name="request">An https URL, the events, and a description.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The webhook and its secret.</returns>
    /// <response code="201">The webhook and its secret.</response>
    /// <response code="400">url-invalid or events-invalid.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks webhooks:manage.</response>
    /// <response code="409">webhooks-too-many.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<CreatedWebhook>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(WebhookRequest request, CancellationToken cancellationToken)
    {
        var (webhook, secret, refusal) = await webhooks.CreateAsync(User.TenantId()!.Value, request, Actor, cancellationToken);

        if (refusal is not null)
        {
            return PublicApi.Problem(HttpContext, refusal.Status, refusal.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/v1/webhooks/{webhook!.Id}", new CreatedWebhook(webhook, secret!));
    }

    /// <summary>Changes a webhook's URL, events or state.</summary>
    /// <remarks>Scope webhooks:manage. active false stops it; active true starts it again, also after it was paused for failing.</remarks>
    /// <param name="id">The webhook.</param>
    /// <param name="request">What changes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The webhook.</returns>
    /// <response code="200">The webhook.</response>
    /// <response code="400">url-invalid or events-invalid.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks webhooks:manage.</response>
    /// <response code="404">webhook-not-found.</response>
    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<WebhookView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Change(Guid id, WebhookChange request, CancellationToken cancellationToken)
    {
        var (webhook, refusal) = await webhooks.ChangeAsync(id, request, Actor, cancellationToken);

        if (refusal is not null)
        {
            return PublicApi.Problem(HttpContext, refusal.Status, refusal.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(webhook);
    }

    /// <summary>Deletes a webhook and its deliveries log.</summary>
    /// <remarks>Scope webhooks:manage.</remarks>
    /// <param name="id">The webhook.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Nothing.</returns>
    /// <response code="204">Deleted.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks webhooks:manage.</response>
    /// <response code="404">webhook-not-found.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await webhooks.DeleteAsync(id, Actor, cancellationToken) is { } refusal)
        {
            return PublicApi.Problem(HttpContext, refusal.Status, refusal.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Lists a webhook's deliveries, newest first.</summary>
    /// <remarks>Scope webhooks:manage. Cursor-based: pass nextCursor as cursor for the next page.</remarks>
    /// <param name="id">The webhook.</param>
    /// <param name="cursor">From the previous page.</param>
    /// <param name="limit">At most 100; 50 when not given.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of deliveries.</returns>
    /// <response code="200">A page of deliveries.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks webhooks:manage.</response>
    /// <response code="404">webhook-not-found.</response>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType<DeliveryPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Deliveries(Guid id, Guid? cursor, int? limit, CancellationToken cancellationToken) =>
        await webhooks.DeliveriesAsync(id, cursor, limit, cancellationToken) is { } page ? Ok(page) : PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "webhook-not-found");

    /// <summary>Sends a delivery's event again.</summary>
    /// <remarks>Scope webhooks:manage. A new delivery with the same event id, so receivers that saw it already can skip it.</remarks>
    /// <param name="id">The webhook.</param>
    /// <param name="deliveryId">The delivery.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Nothing.</returns>
    /// <response code="202">Queued.</response>
    /// <response code="401">unauthorized: no valid API key.</response>
    /// <response code="403">scope-missing: the key lacks webhooks:manage.</response>
    /// <response code="404">webhook-not-found or delivery-not-found.</response>
    [HttpPost("{id:guid}/deliveries/{deliveryId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Replay(Guid id, Guid deliveryId, CancellationToken cancellationToken)
    {
        if (await webhooks.ReplayAsync(id, deliveryId, cancellationToken) is { } refusal)
        {
            return PublicApi.Problem(HttpContext, refusal.Status, refusal.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        jobs.Nudge();

        return Accepted();
    }
}
