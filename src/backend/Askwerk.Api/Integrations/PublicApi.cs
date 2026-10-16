// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Askwerk.Api.Identity;
using Askwerk.Domain.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Askwerk.Api.Integrations;

/// <summary>
/// The Public API's ground rules (AWK-25, public-api.md): API keys as Bearer tokens on /api/v1 only, scopes as
/// policies, problem details with stable types, and a rate limit per key.
/// </summary>
public static class PublicApi
{
    /// <summary>The OpenAPI document and ApiExplorer group of public endpoints.</summary>
    public const string Group = "v1";

    /// <summary>The authentication scheme of API keys.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>The scheme that picks API keys for /api/v1 and the cookie session elsewhere.</summary>
    public const string SelectingScheme = "askwerk";

    /// <summary>Claim with the key's id.</summary>
    public const string KeyClaim = "askwerk:api-key";

    /// <summary>Claim with the OAuth client's id, for calls with an access token (AWK-32).</summary>
    public const string ClientClaim = "askwerk:oauth-client";

    /// <summary>Claim per scope of the key.</summary>
    public const string ScopeClaim = "scope";

    /// <summary>Base of the problem types (W14, decision f).</summary>
    public const string ProblemBase = "https://askwerk.org/problems/";

    /// <summary>Policy: the key carries surveys:read.</summary>
    public const string SurveysRead = "scope:surveys:read";

    /// <summary>Policy: the key carries responses:read.</summary>
    public const string ResponsesRead = "scope:responses:read";

    /// <summary>Policy: any key or OAuth token, whatever its scopes; the MCP server checks each tool's scope (AWK-32).</summary>
    public const string Integration = "integration";

    /// <summary>Policy: the key may manage webhooks (AWK-32).</summary>
    public const string WebhooksManage = "scope:webhooks:manage";

    /// <summary>Policy: the key may provision users (AWK-12).</summary>
    public const string UsersManage = "scope:users:manage";

    /// <summary>Whether a request is for the Public API.</summary>
    /// <param name="request">The request.</param>
    /// <returns>True for /api/v1.</returns>
    public static bool IsPublic(HttpRequest request) => request is not null && (request.Path.StartsWithSegments("/api/v1") || request.Path.StartsWithSegments("/mcp"));

    /// <summary>The key of an authenticated caller.</summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The key, or null.</returns>
    public static Guid? ApiKeyId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(KeyClaim), out var id) ? id : null;

    /// <summary>The OAuth client of a caller with an access token (AWK-32).</summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The client, or null.</returns>
    public static Guid? OAuthClientId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClientClaim), out var id) ? id : null;

    /// <summary>The audit log's actor of a Public API call: the key, or the OAuth client.</summary>
    /// <param name="principal">The caller.</param>
    /// <returns>Kind and id.</returns>
    public static (AuditActorKind Kind, string Id) IntegrationActor(this ClaimsPrincipal principal) =>
        principal.OAuthClientId() is { } client ? (AuditActorKind.OAuthClient, client.ToString()) : (AuditActorKind.ApiKey, principal.ApiKeyId().ToString() ?? "");

    private static bool IsIntegration(AuthorizationHandlerContext context) => context.User.HasClaim(claim => claim.Type is KeyClaim or ClientClaim);

    /// <summary>Adds the key scheme, the scheme that chooses between key and cookie, and the scope policies.</summary>
    /// <param name="services">The services.</param>
    public static void AddPublicApi(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, null)
            .AddScheme<AuthenticationSchemeOptions, Askwerk.Api.Identity.EmbedSessionHandler>(Askwerk.Api.Identity.EmbedSessions.Scheme, null)
            .AddPolicyScheme(SelectingScheme, null, options => options.ForwardDefaultSelector = context => IsPublic(context.Request) ? Scheme : Askwerk.Api.Identity.EmbedSessions.HasToken(context.Request) ? Askwerk.Api.Identity.EmbedSessions.Scheme : IdentityConstants.ApplicationScheme);
        services.Configure<AuthenticationOptions>(options => options.DefaultScheme = SelectingScheme);
        services.AddAuthorizationBuilder()
            .AddPolicy(SurveysRead, policy => policy.AddAuthenticationSchemes(Scheme).RequireAssertion(IsIntegration).RequireClaim(ScopeClaim, "surveys:read"))
            .AddPolicy(ResponsesRead, policy => policy.AddAuthenticationSchemes(Scheme).RequireAssertion(IsIntegration).RequireClaim(ScopeClaim, "responses:read"))
            .AddPolicy(Integration, policy => policy.AddAuthenticationSchemes(Scheme).RequireAssertion(IsIntegration))
            .AddPolicy(WebhooksManage, policy => policy.AddAuthenticationSchemes(Scheme).RequireAssertion(IsIntegration).RequireClaim(ScopeClaim, "webhooks:manage"))
            .AddPolicy(UsersManage, policy => policy.AddAuthenticationSchemes(Scheme).RequireAssertion(IsIntegration).RequireClaim(ScopeClaim, "users:manage"));
    }

    /// <summary>A problem of the Public API as an action result.</summary>
    /// <param name="context">The request.</param>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">The stable code; the type is <see cref="ProblemBase"/> + code.</param>
    /// <returns>The result.</returns>
    public static IActionResult Problem(HttpContext context, int status, string code) => new ProblemResult(status, code);

    /// <summary>Writes the problem itself, so no content negotiation turns it into plain JSON.</summary>
    /// <summary>A Public API problem as an action result; the MCP server reads its status and code.</summary>
    internal sealed class ProblemResult(int status, string code) : IActionResult
    {
        /// <summary>Gets the HTTP status.</summary>
        public int Status => status;

        /// <summary>Gets the stable code.</summary>
        public string Code => code;

        public Task ExecuteResultAsync(ActionContext context) => PublicProblems.WriteAsync(context.HttpContext, status, code);
    }
}

/// <summary>The problem types of the Public API with their titles and details in German and English (docs/api/errors.md).</summary>
public static class PublicProblems
{
    private static readonly Dictionary<string, (string Title, string Detail, string TitelDe, string DetailDe)> Catalogue = new(StringComparer.Ordinal)
    {
        ["unauthorized"] = ("No valid API key.", "Send an API key of Settings → API as Authorization: Bearer <key>. Revoked and expired keys do not work.", "Kein gültiger API-Schlüssel.", "Senden Sie einen Schlüssel aus Einstellungen → API als Authorization: Bearer <Schlüssel>. Widerrufene und abgelaufene Schlüssel gelten nicht."),
        ["scope-missing"] = ("The API key may not do this.", "Create a key with the scope this endpoint names.", "Der API-Schlüssel darf das nicht.", "Erstellen Sie einen Schlüssel mit der Berechtigung, die dieser Endpunkt nennt."),
        ["rate-limited"] = ("Too many calls.", "Wait as long as Retry-After says, then call again.", "Zu viele Aufrufe.", "Warten Sie so lange, wie Retry-After angibt, und rufen Sie dann wieder auf."),
        ["survey-not-found"] = ("The survey does not exist.", "Check the id; the key sees only its organisation's surveys.", "Die Umfrage gibt es nicht.", "Prüfen Sie die Id; der Schlüssel sieht nur die Umfragen seiner Organisation."),
        ["unknown-format"] = ("Unknown export format.", "Use excel, csv, spss, stata or json.", "Unbekanntes Exportformat.", "Verwenden Sie excel, csv, spss, stata oder json."),
        ["not-published"] = ("The survey is not published.", "Publish it before exporting its responses.", "Die Umfrage ist nicht veröffentlicht.", "Veröffentlichen Sie sie, bevor Sie ihre Antworten exportieren."),
        ["too-many-exports"] = ("Three exports are already being prepared.", "Start another one when one of them is ready.", "Es werden schon drei Exporte vorbereitet.", "Starten Sie einen weiteren, sobald einer fertig ist."),
        ["export-not-found"] = ("The export does not exist.", "Check the id; the key sees only its organisation's exports.", "Den Export gibt es nicht.", "Prüfen Sie die Id; der Schlüssel sieht nur die Exporte seiner Organisation."),
        ["export-not-ready"] = ("The export is not ready yet.", "Poll the export until its status is ready.", "Der Export ist noch nicht fertig.", "Fragen Sie den Export ab, bis sein Status ready ist."),
        ["export-expired"] = ("The export's file was deleted.", "Files are kept 7 days. Start a new export.", "Die Datei des Exports wurde gelöscht.", "Dateien bleiben 7 Tage. Starten Sie einen neuen Export."),
        ["export-failed"] = ("The export could not be prepared.", "Nothing was lost. Start a new export; the trace id helps your administrator.", "Der Export konnte nicht vorbereitet werden.", "Nichts ist verloren. Starten Sie einen neuen Export; die Trace-Id hilft Ihrer Administration."),
        ["url-invalid"] = ("The webhook URL is not allowed.", "Use an https URL without a user name or password.", "Die Webhook-URL ist nicht erlaubt.", "Verwenden Sie eine https-URL ohne Benutzername oder Passwort."),
        ["events-invalid"] = ("Unknown or no events.", "Choose from export.completed, response.completed, survey.published and survey.closed.", "Unbekannte oder keine Ereignisse.", "Wählen Sie aus export.completed, response.completed, survey.published und survey.closed."),
        ["webhooks-too-many"] = ("Twenty webhooks already exist.", "Delete one before adding another.", "Es gibt schon zwanzig Webhooks.", "Löschen Sie einen, bevor Sie einen weiteren anlegen."),
        ["webhook-not-found"] = ("The webhook does not exist.", "Check the id; the key sees only its organisation's webhooks.", "Den Webhook gibt es nicht.", "Prüfen Sie die Id; der Schlüssel sieht nur die Webhooks seiner Organisation."),
        ["provisioning-off"] = ("Provisioning is switched off.", "An owner switches it on in Organisation → Sign-in.", "Die Bereitstellung ist ausgeschaltet.", "Eine Inhaberin oder ein Inhaber schaltet sie unter Organisation → Anmeldung ein."),
        ["user-not-found"] = ("The user does not exist.", "Check the externalId; only users provisioned in this organisation are found.", "Den Benutzer gibt es nicht.", "Prüfen Sie die externalId; nur in dieser Organisation bereitgestellte Benutzer werden gefunden."),
        ["user-invalid"] = ("The user is not valid.", "Send email, name and a role of admin, editor, analyst or viewer.", "Der Benutzer ist ungültig.", "Senden Sie email, name und eine Rolle: admin, editor, analyst oder viewer."),
        ["owner-not-provisioned"] = ("Owners are not provisioned.", "Owners keep their Askwerk account; choose admin or a lower role.", "Inhaber werden nicht bereitgestellt.", "Inhaber behalten ihr Askwerk-Konto; wählen Sie admin oder eine niedrigere Rolle."),
        ["delivery-not-found"] = ("The delivery does not exist.", "Check the id in the webhook's deliveries.", "Die Zustellung gibt es nicht.", "Prüfen Sie die Id in den Zustellungen des Webhooks."),
        ["too-many-rows-for-excel"] = ("Too many rows for one Excel sheet.", "Export as csv instead.", "Zu viele Zeilen für ein Excel-Blatt.", "Exportieren Sie stattdessen als csv."),
    };

    /// <summary>A problem in the caller's language (Accept-Language: de… for German, else English).</summary>
    /// <param name="context">The request.</param>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">The stable code.</param>
    /// <returns>The problem.</returns>
    public static ProblemDetails Of(HttpContext context, int? status, string code)
    {
        ArgumentNullException.ThrowIfNull(context);
        var german = context.Request.Headers.AcceptLanguage.ToString().TrimStart().StartsWith("de", StringComparison.OrdinalIgnoreCase);
        var (title, detail, titelDe, detailDe) = Catalogue.TryGetValue(code, out var known) ? known : (code, "", code, "");
        var problem = new ProblemDetails
        {
            Type = PublicApi.ProblemBase + code,
            Title = german ? titelDe : title,
            Detail = german ? detailDe : detail,
            Status = status,
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString();

        return problem;
    }

    /// <summary>Writes a problem as the whole response.</summary>
    /// <param name="context">The request.</param>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">The stable code.</param>
    /// <returns>When written.</returns>
    public static Task WriteAsync(HttpContext context, int status, string code)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(Of(context, status, code), (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

/// <summary>
/// Authenticates API keys (Authorization: Bearer awk_…): finds the key by its prefix across organisations, checks
/// the secret in constant time and that it is usable, and works in the key's organisation from then on. Notes the
/// last use at most once a minute.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider services,
    TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var secret = header["Bearer ".Length..].Trim();

        if (secret.Length == 48 && secret.StartsWith("awt_", StringComparison.Ordinal))
        {
            return await AccessTokenAsync(secret);
        }

        if (secret.Length != 48 || !secret.StartsWith("awk_", StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Not an API key.");
        }

        // Resolved only now: without a key nothing needs the database (e.g. the OpenAPI document).
        var database = services.GetRequiredService<AskwerkDbContext>();
        var session = services.GetRequiredService<DbSessionContext>();
        var now = time.GetUtcNow();
        var key = await database.FindApiKeyAsync(secret[..12], Context.RequestAborted);

        if (key is null || !key.Matches(secret) || !key.IsUsable(now))
        {
            return AuthenticateResult.Fail("Unknown, revoked or expired API key.");
        }

        // From here on the request works in the key's organisation and as no user, whatever else it carries.
        session.TenantId = key.TenantId;
        session.UserId = null;

        if (!await services.GetRequiredService<TenantStates>().IsOpenAsync(key.TenantId, database, Context.RequestAborted))
        {
            return AuthenticateResult.Fail("The organisation is suspended.");
        }

        if (key.Used(now))
        {
            await database.ApiKeys.Where(row => row.Id == key.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.LastUsedAt, now), Context.RequestAborted);
        }

        var claims = new List<Claim> { new(PublicApi.KeyClaim, key.Id.ToString()), new(Sessions.TenantClaim, key.TenantId.ToString()) };
        claims.AddRange(key.Scopes.Select(scope => new Claim(PublicApi.ScopeClaim, scope)));

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }

    /// <summary>An OAuth access token (AWK-32): found by its hash across organisations, working while it and its client do.</summary>
    private async Task<AuthenticateResult> AccessTokenAsync(string value)
    {
        var database = services.GetRequiredService<AskwerkDbContext>();
        var session = services.GetRequiredService<DbSessionContext>();
        var hash = Askwerk.Domain.Integrations.AccessToken.HashOf(value);
        var token = await database.AccessTokens.FromSql($"SELECT * FROM access_token_lookup({hash})").AsNoTracking().SingleOrDefaultAsync(Context.RequestAborted);

        if (token is null || token.ExpiresAt <= time.GetUtcNow())
        {
            return AuthenticateResult.Fail("Unknown or expired access token, or its client was revoked.");
        }

        session.TenantId = token.TenantId;
        session.UserId = null;

        if (!await services.GetRequiredService<TenantStates>().IsOpenAsync(token.TenantId, database, Context.RequestAborted))
        {
            return AuthenticateResult.Fail("The organisation is suspended.");
        }

        var claims = new List<Claim> { new(PublicApi.ClientClaim, token.ClientId.ToString()), new(Sessions.TenantClaim, token.TenantId.ToString()) };
        claims.AddRange(token.Scopes.Select(scope => new Claim(PublicApi.ScopeClaim, scope)));

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        PublicProblems.WriteAsync(Context, StatusCodes.Status401Unauthorized, "unauthorized");

    /// <inheritdoc />
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        PublicProblems.WriteAsync(Context, StatusCodes.Status403Forbidden, "scope-missing");
}

/// <summary>
/// A fixed one-minute window of calls per API key on /api/v1 (600 by default, ASKWERK_API_RATE_LIMIT), with
/// RateLimit-Limit, RateLimit-Remaining and RateLimit-Reset on every answer and Retry-After on a 429. Counted per
/// instance; with several instances each counts its own calls.
/// </summary>
public sealed class PublicApiRateLimit(RequestDelegate next, IConfiguration configuration, TimeProvider time)
{
    private readonly int limit = int.TryParse(configuration["ASKWERK_API_RATE_LIMIT"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured > 0 ? configured : 600;
    private readonly ConcurrentDictionary<string, Window> windows = new(StringComparer.Ordinal);

    /// <summary>Counts the call and refuses it once the window is full.</summary>
    /// <param name="context">The request.</param>
    /// <returns>When handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!PublicApi.IsPublic(context.Request))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var caller = (header.StartsWith("Bearer awk_", StringComparison.OrdinalIgnoreCase) || header.StartsWith("Bearer awt_", StringComparison.OrdinalIgnoreCase)) && header.Length >= "Bearer ".Length + 12
            ? string.Concat("key:", header.AsSpan("Bearer ".Length, 12))
            : "ip:" + context.Connection.RemoteIpAddress;
        var now = time.GetUtcNow();
        var window = windows.AddOrUpdate(
            caller,
            _ => new Window(now, 1),
            (_, current) => now - current.Start >= TimeSpan.FromMinutes(1) ? new Window(now, 1) : current with { Count = current.Count + 1 });
        var reset = (int)Math.Ceiling((window.Start.AddMinutes(1) - now).TotalSeconds);
        context.Response.Headers["RateLimit-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["RateLimit-Remaining"] = Math.Max(0, limit - window.Count).ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["RateLimit-Reset"] = reset.ToString(CultureInfo.InvariantCulture);

        if (window.Count > limit)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, reset).ToString(CultureInfo.InvariantCulture);
            await PublicProblems.WriteAsync(context, StatusCodes.Status429TooManyRequests, "rate-limited");
            return;
        }

        await next(context);
    }

    private sealed record Window(DateTimeOffset Start, int Count);
}
