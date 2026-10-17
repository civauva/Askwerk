// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.Identity;
using Askwerk.Api.Integrations;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Connections;

/// <summary>
/// Administration → Connections (W36, AWK-49): the systems the organisation works with. Connect a system in three
/// steps sets up what its goals need (an OAuth client or API key, a webhook) and shows the secrets once; the list says
/// whether each works. Owners and admins only; every change is audited.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/connections")]
public sealed class ConnectionsController(
    AskwerkDbContext context,
    WebhookService webhooks,
    ISecretProtector secrets,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    JobRunner jobs,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    /// <summary>How long before a key or client ends the list warns.</summary>
    private static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(14);

    private WebhookActor Actor => new(AuditActorKind.User, User.UserId().ToString()!);

    /// <summary>The organisation's connections, newest first, with whether each works.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The connections; 403 role-not-allowed.</returns>
    [HttpGet]
    [ProducesResponseType<ConnectionList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var connections = await context.Connections.AsNoTracking().OrderByDescending(connection => connection.CreatedAt).ToListAsync(cancellationToken);

        return Ok(new ConnectionList(await ViewsAsync(connections, cancellationToken)));
    }

    /// <summary>One connection.</summary>
    /// <param name="id">The connection.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The connection; 403; 404.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        return await context.Connections.AsNoTracking().FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken) is { } connection
            ? Ok((await ViewsAsync([connection], cancellationToken))[0])
            : NotFound(new ConnectionProblem("connection-not-found"));
    }

    /// <summary>
    /// Connects a system: sets up what its goals need and returns the secrets, in this answer only. Answers with a URL
    /// get a webhook; people, or answers without a URL, get an OAuth client or API key with the chosen permissions.
    /// </summary>
    /// <param name="request">Name, goals and details.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the connection and its secrets; 400 at the field; 403; 409 connections-too-many or webhooks-too-many.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedConnection>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(ConnectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.Connections.CountAsync(cancellationToken) >= Connection.MaxPerOrganisation)
        {
            return Conflict(new ConnectionProblem("connections-too-many"));
        }

        var goals = request.Goals ?? [];
        var webhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim();
        var wantsWebhook = goals.Contains(ConnectionGoals.Answers) && webhookUrl is not null;
        var wantsCalls = goals.Contains(ConnectionGoals.People) || (goals.Contains(ConnectionGoals.Answers) && webhookUrl is null);
        var permissions = request.Permissions ?? [];

        if (wantsCalls && (permissions.Count == 0 || permissions.Any(permission => !ConnectionPermissions.All.Contains(permission))))
        {
            return Invalid("permissions", "permissions-invalid");
        }

        if (wantsCalls && request.SignIn is not (ConnectionSignIn.OAuth or ConnectionSignIn.ApiKey))
        {
            return Invalid("signIn", "sign-in-invalid");
        }

        if (request.SurveyIds is { Count: > 0 } surveyIds && await context.Surveys.CountAsync(survey => surveyIds.Contains(survey.Id), cancellationToken) != surveyIds.Distinct().Count())
        {
            return Invalid("surveyIds", "survey-not-found");
        }

        var now = time.GetUtcNow();
        var userId = User.UserId()!.Value;
        var tenantId = User.TenantId()!.Value;
        Connection connection;

        try
        {
            connection = Connection.Create(Guid.CreateVersion7(now), tenantId, request.Name ?? "", goals, request.MatchBy, userId, now);
        }
        catch (DomainValidationException invalid)
        {
            return Invalid(invalid.Field, invalid.Code);
        }

        var credentials = new ConnectionCredentials(null, null, null, null);
        Guid? apiKeyId = null, clientId = null, webhookId = null;

        if (wantsWebhook)
        {
            var events = request.Events is { Count: > 0 } chosen ? chosen : ["response.completed"];
            var (webhook, secret, refusal) = await webhooks.CreateAsync(tenantId, new WebhookRequest(webhookUrl, events, connection.Name), Actor, cancellationToken);

            if (refusal is not null)
            {
                return refusal.Status == StatusCodes.Status400BadRequest
                    ? Invalid(refusal.Code == "url-invalid" ? "webhookUrl" : "events", refusal.Code)
                    : StatusCode(refusal.Status, new ConnectionProblem(refusal.Code));
            }

            webhookId = webhook!.Id;
            context.Webhooks.Local.First(each => each.Id == webhookId).ForSurveys(request.SurveyIds ?? []);
            credentials = credentials with { WebhookSecret = secret };
        }

        if (wantsCalls)
        {
            var scopes = ConnectionPermissions.ScopesOf(permissions);

            if (request.SignIn == ConnectionSignIn.OAuth)
            {
                var (client, secret) = OAuthClient.Issue(Guid.CreateVersion7(now), tenantId, connection.Name, scopes, null, userId, now);
                context.OAuthClients.Add(client);
                clientId = client.Id;
                credentials = credentials with { ClientId = client.ClientId, ClientSecret = secret };
                await AuditAsync(tenantId, "oauth-client.created", "OAuthClient", client.Id, $"{client.Name}: {string.Join(' ', client.Scopes)}", cancellationToken);
            }
            else
            {
                var (key, secret) = ApiKey.Issue(Guid.CreateVersion7(now), tenantId, connection.Name, scopes, null, userId, now);
                context.ApiKeys.Add(key);
                apiKeyId = key.Id;
                credentials = credentials with { ApiKey = secret };
                await AuditAsync(tenantId, "api-key.created", "ApiKey", key.Id, $"{key.Name}: {string.Join(' ', key.Scopes)}", cancellationToken);
            }
        }

        connection.Link(apiKeyId, clientId, webhookId);
        context.Connections.Add(connection);
        await AuditAsync(tenantId, "connection.created", "Connection", connection.Id, $"{connection.Name}: {string.Join(' ', connection.Goals)}", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CreatedConnection((await ViewsAsync([connection], cancellationToken))[0], credentials));
    }

    /// <summary>Changes the name, how people are matched, the webhook's URL and events, or its surveys.</summary>
    /// <param name="id">The connection.</param>
    /// <param name="request">What changes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The connection; 400; 403; 404.</returns>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Change(Guid id, ConnectionChange request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.Connections.FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return NotFound(new ConnectionProblem("connection-not-found"));
        }

        try
        {
            connection.Change(request.Name, request.MatchBy);
        }
        catch (DomainValidationException invalid)
        {
            return Invalid(invalid.Field, invalid.Code);
        }

        if (connection.WebhookId is { } webhookId && (request.WebhookUrl is not null || request.Events is not null || request.SurveyIds is not null))
        {
            if (request.SurveyIds is { Count: > 0 } surveyIds && await context.Surveys.CountAsync(survey => surveyIds.Contains(survey.Id), cancellationToken) != surveyIds.Distinct().Count())
            {
                return Invalid("surveyIds", "survey-not-found");
            }

            var (_, refusal) = await webhooks.ChangeAsync(webhookId, new WebhookChange(request.WebhookUrl, request.Events), Actor, cancellationToken);

            if (refusal is not null)
            {
                return refusal.Status == StatusCodes.Status400BadRequest ? Invalid(refusal.Code == "url-invalid" ? "webhookUrl" : "events", refusal.Code) : StatusCode(refusal.Status, new ConnectionProblem(refusal.Code));
            }

            if (request.SurveyIds is not null)
            {
                (await context.Webhooks.FirstAsync(each => each.Id == webhookId, cancellationToken)).ForSurveys(request.SurveyIds);
            }
        }

        await AuditAsync(connection.TenantId, "connection.updated", "Connection", connection.Id, connection.Name, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok((await ViewsAsync([connection], cancellationToken))[0]);
    }

    /// <summary>Sends a test event to the connection's webhook.</summary>
    /// <param name="id">The connection.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202; 403; 404; 409 no-webhook or connection-paused.</returns>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Test(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.Connections.AsNoTracking().FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return NotFound(new ConnectionProblem("connection-not-found"));
        }

        if (connection.PausedAt is not null)
        {
            return Conflict(new ConnectionProblem("connection-paused"));
        }

        if (connection.WebhookId is not { } webhookId || await webhooks.PingAsync(webhookId, cancellationToken) is not null)
        {
            return Conflict(new ConnectionProblem("no-webhook"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        jobs.Nudge();

        return Accepted();
    }

    /// <summary>New secrets for the connection: the old ones stop working at once. Shown in this answer only.</summary>
    /// <param name="id">The connection.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new secrets; 403; 404.</returns>
    [HttpPost("{id:guid}/secrets")]
    [ProducesResponseType<ConnectionCredentials>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> NewSecrets(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.Connections.AsNoTracking().FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return NotFound(new ConnectionProblem("connection-not-found"));
        }

        var credentials = new ConnectionCredentials(null, null, null, null);

        if (connection.ApiKeyId is { } keyId && await context.ApiKeys.FirstOrDefaultAsync(key => key.Id == keyId && key.RevokedAt == null, cancellationToken) is { } key)
        {
            credentials = credentials with { ApiKey = key.NewSecret() };
        }

        if (connection.OAuthClientId is { } clientId && await context.OAuthClients.FirstOrDefaultAsync(client => client.Id == clientId && client.RevokedAt == null, cancellationToken) is { } client)
        {
            credentials = credentials with { ClientId = client.ClientId, ClientSecret = client.NewSecret() };
        }

        if (connection.WebhookId is { } webhookId && await context.Webhooks.FirstOrDefaultAsync(webhook => webhook.Id == webhookId, cancellationToken) is { } webhook)
        {
            var secret = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            webhook.NewSecret(secrets.Protect(secret));
            credentials = credentials with { WebhookSecret = secret };
        }

        await AuditAsync(connection.TenantId, "connection.secrets-changed", "Connection", connection.Id, connection.Name, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(credentials);
    }

    /// <summary>Pauses the connection, both directions, or lets it work again.</summary>
    /// <param name="id">The connection.</param>
    /// <param name="request">Whether it is paused.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The connection; 403; 404.</returns>
    [HttpPut("{id:guid}/paused")]
    [ProducesResponseType<ConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pause(Guid id, ConnectionPause request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.Connections.FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return NotFound(new ConnectionProblem("connection-not-found"));
        }

        var now = time.GetUtcNow();
        connection.Pause(request.Paused, User.UserId()!.Value, now);

        if (connection.ApiKeyId is { } keyId && await context.ApiKeys.FirstOrDefaultAsync(key => key.Id == keyId, cancellationToken) is { } key)
        {
            key.Pause(request.Paused, now);
        }

        if (connection.OAuthClientId is { } clientId && await context.OAuthClients.FirstOrDefaultAsync(client => client.Id == clientId, cancellationToken) is { } client)
        {
            client.Pause(request.Paused, now);
        }

        if (connection.WebhookId is { } webhookId && await context.Webhooks.FirstOrDefaultAsync(webhook => webhook.Id == webhookId, cancellationToken) is { } webhook)
        {
            webhook.Change(null, null, !request.Paused);
        }

        await AuditAsync(connection.TenantId, request.Paused ? "connection.paused" : "connection.resumed", "Connection", connection.Id, connection.Name, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok((await ViewsAsync([connection], cancellationToken))[0]);
    }

    /// <summary>Removes the connection: its key or client is revoked and its webhook deleted.</summary>
    /// <param name="id">The connection.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ConnectionProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await context.Connections.FirstOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return NotFound(new ConnectionProblem("connection-not-found"));
        }

        var now = time.GetUtcNow();

        if (connection.ApiKeyId is { } keyId && await context.ApiKeys.FirstOrDefaultAsync(key => key.Id == keyId, cancellationToken) is { } key)
        {
            key.Revoke(now);
            await AuditAsync(connection.TenantId, "api-key.revoked", "ApiKey", key.Id, key.Name, cancellationToken);
        }

        if (connection.OAuthClientId is { } clientId && await context.OAuthClients.FirstOrDefaultAsync(client => client.Id == clientId, cancellationToken) is { } client)
        {
            client.Revoke(now);
            await AuditAsync(connection.TenantId, "oauth-client.revoked", "OAuthClient", client.Id, client.Name, cancellationToken);
        }

        context.Connections.Remove(connection);

        if (connection.WebhookId is { } webhookId)
        {
            await webhooks.DeleteAsync(webhookId, Actor, cancellationToken);
        }

        await AuditAsync(connection.TenantId, "connection.removed", "Connection", connection.Id, connection.Name, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IReadOnlyList<ConnectionView>> ViewsAsync(IReadOnlyList<Connection> connections, CancellationToken cancellationToken)
    {
        var keyIds = connections.Select(connection => connection.ApiKeyId).OfType<Guid>().ToList();
        var clientIds = connections.Select(connection => connection.OAuthClientId).OfType<Guid>().ToList();
        var webhookIds = connections.Select(connection => connection.WebhookId).OfType<Guid>().ToList();
        var userIds = connections.Select(connection => connection.PausedBy).OfType<Guid>().ToList();
        var now = time.GetUtcNow();
        var weekAgo = now.AddDays(-7);

        var keys = await context.ApiKeys.AsNoTracking().Where(key => keyIds.Contains(key.Id)).ToDictionaryAsync(key => key.Id, cancellationToken);
        var clients = await context.OAuthClients.AsNoTracking().Where(client => clientIds.Contains(client.Id)).ToDictionaryAsync(client => client.Id, cancellationToken);
        var hooks = await context.Webhooks.AsNoTracking().Where(webhook => webhookIds.Contains(webhook.Id)).ToDictionaryAsync(webhook => webhook.Id, cancellationToken);
        var users = await context.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        var deliveries = await context.WebhookDeliveries.AsNoTracking()
            .Where(delivery => webhookIds.Contains(delivery.WebhookId))
            .GroupBy(delivery => delivery.WebhookId)
            .Select(group => new
            {
                WebhookId = group.Key,
                Week = group.Count(delivery => delivery.Status == WebhookDeliveryStatus.Delivered && delivery.CreatedAt >= weekAgo),
                Failed = group.Count(delivery => delivery.Status == WebhookDeliveryStatus.Failed && delivery.CreatedAt >= weekAgo),
                LastDelivered = group.Max(delivery => delivery.DeliveredAt),
            })
            .ToDictionaryAsync(row => row.WebhookId, cancellationToken);
        var surveyIds = hooks.Values.SelectMany(webhook => webhook.SurveyIds).Distinct().ToList();
        var surveys = await context.Surveys.AsNoTracking().Where(survey => surveyIds.Contains(survey.Id)).ToDictionaryAsync(survey => survey.Id, survey => survey.Title, cancellationToken);

        return [.. connections.Select(connection =>
        {
            var key = connection.ApiKeyId is { } keyId ? keys.GetValueOrDefault(keyId) : null;
            var client = connection.OAuthClientId is { } clientId ? clients.GetValueOrDefault(clientId) : null;
            var webhook = connection.WebhookId is { } webhookId ? hooks.GetValueOrDefault(webhookId) : null;
            var delivered = webhook is null ? null : deliveries.GetValueOrDefault(webhook.Id);
            var calls = client is not null
                ? new ConnectionCalls(ConnectionSignIn.OAuth, client.ClientId, ConnectionPermissions.Of(client.Scopes), client.LastUsedAt, client.ExpiresAt, client.RevokedAt is not null)
                : key is not null
                    ? new ConnectionCalls(ConnectionSignIn.ApiKey, key.Shown, ConnectionPermissions.Of(key.Scopes), key.LastUsedAt, key.ExpiresAt, key.RevokedAt is not null)
                    : null;
            var hook = webhook is null ? null : new ConnectionWebhook(
                webhook.Url,
                webhook.Subscribed,
                [.. webhook.SurveyIds.Select(id => new ConnectionSurvey(id, surveys.GetValueOrDefault(id) ?? ""))],
                delivered?.Week ?? 0,
                delivered?.Failed ?? 0,
                delivered?.LastDelivered,
                webhook.FailingSince);
            var (health, reason) = HealthOf(connection, calls, hook, webhook, now);
            var lastActivity = new[] { calls?.LastCallAt, hook?.LastDeliveredAt }.Max();

            return new ConnectionView(
                connection.Id,
                connection.Name,
                connection.Goals,
                connection.MatchBy,
                health,
                reason,
                lastActivity,
                connection.PausedAt,
                connection.PausedBy is { } by ? users.GetValueOrDefault(by) : null,
                calls,
                hook,
                connection.CreatedAt);
        })];
    }

    private static (string Health, string? Reason) HealthOf(Connection connection, ConnectionCalls? calls, ConnectionWebhook? hook, Webhook? webhook, DateTimeOffset now)
    {
        if (connection.PausedAt is not null)
        {
            return ("paused", null);
        }

        if (calls is { Revoked: true } || (calls?.ExpiresAt is { } ended && ended <= now))
        {
            return ("failing", "credentials-ended");
        }

        if (webhook is { PausedAt: not null })
        {
            return ("failing", "webhook-paused");
        }

        if (hook?.FailingSince is not null)
        {
            return ("failing", "deliveries-failing");
        }

        if (calls?.ExpiresAt is { } ends && ends - now <= ExpiryWarning)
        {
            return ("warning", "credentials-ending");
        }

        if (hook is { FailedThisWeek: > 0 })
        {
            return ("warning", "deliveries-failed");
        }

        return (calls?.LastCallAt ?? hook?.LastDeliveredAt) is null && (calls is not null || hook is not null) ? ("waiting", null) : ("works", null);
    }

    private BadRequestObjectResult Invalid(string field, string code) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [code] }));

    private Task AuditAsync(Guid tenantId, string action, string kind, Guid id, string details, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, User.UserId().ToString(), action, kind, id.ToString(), details, trace.CurrentTraceId), cancellationToken);

    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is TenantRole.Owner or TenantRole.Admin ? null : StatusCode(StatusCodes.Status403Forbidden, new ConnectionProblem("role-not-allowed"));
    }
}

/// <summary>How a connection's system signs in to call Askwerk.</summary>
public static class ConnectionSignIn
{
    /// <summary>Client ID and secret (OAuth client credentials).</summary>
    public const string OAuth = "oauth";

    /// <summary>An API key.</summary>
    public const string ApiKey = "api-key";
}

/// <summary>Why a connection change was refused.</summary>
/// <param name="Code">role-not-allowed, connection-not-found, connections-too-many, webhooks-too-many, no-webhook or connection-paused.</param>
public sealed record ConnectionProblem(string Code);

/// <summary>Connect a system: what the wizard collected.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Goals">answers, people, sign-in, embed.</param>
/// <param name="WebhookUrl">Where answers go; without it the system reads them.</param>
/// <param name="Events">The webhook's events; response.completed when empty.</param>
/// <param name="SurveyIds">The webhook's surveys; all, also new ones, when empty.</param>
/// <param name="SignIn">oauth or api-key, when it calls Askwerk.</param>
/// <param name="Permissions">people, results, surveys.</param>
/// <param name="MatchBy">external-id (then email) or email.</param>
public sealed record ConnectionRequest(
    string? Name,
    IReadOnlyList<string>? Goals,
    string? WebhookUrl = null,
    IReadOnlyList<string>? Events = null,
    IReadOnlyList<Guid>? SurveyIds = null,
    string? SignIn = null,
    IReadOnlyList<string>? Permissions = null,
    string? MatchBy = null);

/// <summary>What changes; null keeps.</summary>
/// <param name="Name">The name.</param>
/// <param name="MatchBy">How people are matched.</param>
/// <param name="WebhookUrl">The webhook's URL.</param>
/// <param name="Events">The webhook's events.</param>
/// <param name="SurveyIds">The webhook's surveys; empty for all.</param>
public sealed record ConnectionChange(string? Name = null, string? MatchBy = null, string? WebhookUrl = null, IReadOnlyList<string>? Events = null, IReadOnlyList<Guid>? SurveyIds = null);

/// <summary>Whether a connection is paused.</summary>
/// <param name="Paused">True to pause.</param>
public sealed record ConnectionPause(bool Paused);

/// <summary>The secrets of a connection, shown once; null where it has none.</summary>
/// <param name="ClientId">The OAuth client id.</param>
/// <param name="ClientSecret">The OAuth client secret.</param>
/// <param name="ApiKey">The API key.</param>
/// <param name="WebhookSecret">The webhook's signing secret.</param>
public sealed record ConnectionCredentials(string? ClientId, string? ClientSecret, string? ApiKey, string? WebhookSecret);

/// <summary>A connection just created, with its secrets.</summary>
/// <param name="Connection">The connection.</param>
/// <param name="Credentials">Its secrets, shown this once.</param>
public sealed record CreatedConnection(ConnectionView Connection, ConnectionCredentials Credentials);

/// <summary>The organisation's connections.</summary>
/// <param name="Items">Newest first.</param>
public sealed record ConnectionList(IReadOnlyList<ConnectionView> Items);

/// <summary>A survey a webhook is limited to.</summary>
/// <param name="Id">The survey.</param>
/// <param name="Title">Its title.</param>
public sealed record ConnectionSurvey(Guid Id, string Title);

/// <summary>How the system calls Askwerk.</summary>
/// <param name="SignIn">oauth or api-key.</param>
/// <param name="Shown">The client id, or the key's first characters.</param>
/// <param name="Permissions">people, results, surveys.</param>
/// <param name="LastCallAt">Its last call or token (UTC).</param>
/// <param name="ExpiresAt">When it ends; never when empty.</param>
/// <param name="Revoked">Whether it was revoked elsewhere.</param>
public sealed record ConnectionCalls(string SignIn, string Shown, IReadOnlyList<string> Permissions, DateTimeOffset? LastCallAt, DateTimeOffset? ExpiresAt, bool Revoked);

/// <summary>How Askwerk calls the system.</summary>
/// <param name="Url">Where events go.</param>
/// <param name="Events">The events.</param>
/// <param name="Surveys">The surveys; all when empty.</param>
/// <param name="DeliveredThisWeek">Events delivered in the last 7 days.</param>
/// <param name="FailedThisWeek">Events that failed in the last 7 days.</param>
/// <param name="LastDeliveredAt">The last delivered event (UTC).</param>
/// <param name="FailingSince">Since when every delivery failed.</param>
public sealed record ConnectionWebhook(string Url, IReadOnlyList<string> Events, IReadOnlyList<ConnectionSurvey> Surveys, int DeliveredThisWeek, int FailedThisWeek, DateTimeOffset? LastDeliveredAt, DateTimeOffset? FailingSince);

/// <summary>A connection as Connections shows it.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Goals">What it is for.</param>
/// <param name="MatchBy">How people are matched.</param>
/// <param name="Health">works, waiting (nothing yet), warning, failing or paused.</param>
/// <param name="Reason">Why warning or failing: credentials-ending, credentials-ended, deliveries-failed, deliveries-failing, webhook-paused.</param>
/// <param name="LastActivityAt">The last call or delivery (UTC).</param>
/// <param name="PausedAt">When it was paused.</param>
/// <param name="PausedBy">Who paused it.</param>
/// <param name="Calls">How it calls Askwerk; null when it does not.</param>
/// <param name="Webhook">How Askwerk calls it; null when it does not.</param>
/// <param name="CreatedAt">Created (UTC).</param>
public sealed record ConnectionView(
    Guid Id,
    string Name,
    IReadOnlyList<string> Goals,
    string MatchBy,
    string Health,
    string? Reason,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? PausedAt,
    string? PausedBy,
    ConnectionCalls? Calls,
    ConnectionWebhook? Webhook,
    DateTimeOffset CreatedAt);
