// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Integrations;

/// <summary>
/// A webhook of an organisation (AWK-32, C32 A): an https URL that gets the chosen events, signed with its secret.
/// Failing for 3 days pauses it; Resume starts it again.
/// </summary>
public sealed class Webhook
{
    /// <summary>The events that can be subscribed now (owner decision, C32).</summary>
    public static readonly IReadOnlyList<string> Events = ["export.completed", "response.completed", "survey.published", "survey.closed"];

    /// <summary>How long a webhook may fail before it is paused.</summary>
    public static readonly TimeSpan PauseAfter = TimeSpan.FromDays(3);

    /// <summary>Most webhooks per organisation.</summary>
    public const int MaxPerOrganisation = 20;

    // For EF Core.
    private Webhook()
    {
        Url = "";
        Subscribed = [];
        SecretProtected = "";
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the URL the events are posted to.</summary>
    public string Url { get; private set; }

    /// <summary>Gets the subscribed events.</summary>
    public string[] Subscribed { get; private set; }

    /// <summary>Gets a note on what it is for.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the signing secret, encrypted with the instance's keys.</summary>
    public string SecretProtected { get; private set; }

    /// <summary>Gets a value indicating whether events are sent.</summary>
    public bool Active { get; private set; }

    /// <summary>Gets when it was paused for failing; null when not paused.</summary>
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>Gets since when every delivery failed; null after a success.</summary>
    public DateTimeOffset? FailingSince { get; private set; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether a URL may receive events: https, or http://localhost when allowed (development).</summary>
    /// <param name="url">The URL.</param>
    /// <param name="allowLocalhost">Allow http://localhost.</param>
    /// <returns>True when it may.</returns>
    public static bool IsValidUrl(string? url, bool allowLocalhost) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (allowLocalhost && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Creates a webhook.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="url">A checked URL.</param>
    /// <param name="events">Checked events.</param>
    /// <param name="description">Optional note.</param>
    /// <param name="secretProtected">The encrypted secret.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The webhook.</returns>
    public static Webhook Create(Guid id, Guid tenantId, string url, IEnumerable<string> events, string? description, string secretProtected, DateTimeOffset now) =>
        new() { Id = id, TenantId = tenantId, Url = url, Subscribed = [.. events.Distinct()], Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(), SecretProtected = secretProtected, Active = true, CreatedAt = now };

    /// <summary>Changes the URL, the events or whether it is active; activating clears a pause.</summary>
    /// <param name="url">New URL, or null to keep.</param>
    /// <param name="events">New events, or null to keep.</param>
    /// <param name="active">New state, or null to keep.</param>
    public void Change(string? url, IEnumerable<string>? events, bool? active)
    {
        Url = url ?? Url;
        Subscribed = events is null ? Subscribed : [.. events.Distinct()];

        if (active is { } on)
        {
            Active = on;

            if (on)
            {
                PausedAt = null;
                FailingSince = null;
            }
        }
    }

    /// <summary>Notes a delivered event.</summary>
    public void Delivered() => FailingSince = null;

    /// <summary>Notes a failed delivery; pauses the webhook when it has failed for 3 days.</summary>
    /// <param name="now">The time of the failure.</param>
    /// <returns>True when it was paused now.</returns>
    public bool Failed(DateTimeOffset now)
    {
        FailingSince ??= now;

        if (now - FailingSince.Value < PauseAfter)
        {
            return false;
        }

        Active = false;
        PausedAt = now;

        return true;
    }
}

/// <summary>Where a delivery stands.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting for its first or next try.</summary>
    Pending,

    /// <summary>The receiver answered 2xx.</summary>
    Delivered,

    /// <summary>Every try failed, or the webhook was paused.</summary>
    Failed,
}

/// <summary>One event on its way to one webhook, with its tries (C32 A, the deliveries log).</summary>
public sealed class WebhookDelivery
{
    /// <summary>When the next try comes after each failed one: 1 min, 5 min, 30 min, 2 h, 6 h, 12 h; then it has failed.</summary>
    public static readonly IReadOnlyList<TimeSpan> Retries = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12)];

    // For EF Core.
    private WebhookDelivery()
    {
        EventId = "";
        EventType = "";
        Payload = "";
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the webhook.</summary>
    public Guid WebhookId { get; private set; }

    /// <summary>Gets the event's id (evt_…); a replay keeps it, so receivers can deduplicate.</summary>
    public string EventId { get; private set; }

    /// <summary>Gets the event type.</summary>
    public string EventType { get; private set; }

    /// <summary>Gets the JSON body as sent.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>Gets how many tries were made.</summary>
    public int Attempts { get; private set; }

    /// <summary>Gets the last HTTP status answered; null when nothing answered.</summary>
    public int? ResponseStatus { get; private set; }

    /// <summary>Gets why the last try failed without an answer.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when it was delivered.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Creates a delivery.</summary>
    /// <param name="id">New id.</param>
    /// <param name="webhook">The webhook.</param>
    /// <param name="eventId">The event's id.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="payload">The JSON body.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The delivery.</returns>
    public static WebhookDelivery Create(Guid id, Webhook webhook, string eventId, string eventType, string payload, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        return new() { Id = id, TenantId = webhook.TenantId, WebhookId = webhook.Id, EventId = eventId, EventType = eventType, Payload = payload, Status = WebhookDeliveryStatus.Pending, CreatedAt = now };
    }

    /// <summary>Notes a successful try.</summary>
    /// <param name="status">The 2xx status.</param>
    /// <param name="now">When.</param>
    public void Succeeded(int status, DateTimeOffset now)
    {
        Attempts++;
        ResponseStatus = status;
        Error = null;
        Status = WebhookDeliveryStatus.Delivered;
        DeliveredAt = now;
    }

    /// <summary>Notes a failed try; returns when to try again, or null when it has failed for good.</summary>
    /// <param name="status">The status answered, if any.</param>
    /// <param name="error">Why nothing was answered.</param>
    /// <param name="giveUp">Stop now, e.g. because the webhook was paused.</param>
    /// <returns>The delay before the next try, or null.</returns>
    public TimeSpan? Tried(int? status, string? error, bool giveUp)
    {
        Attempts++;
        ResponseStatus = status;
        Error = error is { Length: > 500 } ? error[..500] : error;

        if (giveUp || Attempts > Retries.Count)
        {
            Status = WebhookDeliveryStatus.Failed;
            return null;
        }

        Status = WebhookDeliveryStatus.Pending;
        return Retries[Attempts - 1];
    }
}
