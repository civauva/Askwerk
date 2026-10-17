// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Askwerk.Api.Startup;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Askwerk.Api.Identity;

/// <summary>
/// The designer and Results inside a host product (AWK-13, W21): a host's hand-over token for one survey starts an
/// embedded session; the frame keeps its bearer token (aws_…) in memory, never in a third-party cookie, and may reach
/// only that survey.
/// </summary>
public static class EmbedSessions
{
    /// <summary>The authentication scheme of embedded sessions.</summary>
    public const string Scheme = "EmbedSession";

    /// <summary>The claim naming the one survey an embedded session may open.</summary>
    public const string SurveyClaim = "askwerk:embed-survey";

    /// <summary>The survey claim of a survey-list session (W37): no survey, only the list.</summary>
    public static readonly string ListSurvey = Guid.Empty.ToString();

    /// <summary>What an embedded session shows: the designer, Results, the survey list, or one key result (W37).</summary>
    public static readonly string[] Views = ["designer", "results", "list", "result"];

    /// <summary>Whether a request carries an embedded session's token.</summary>
    public static bool HasToken(HttpRequest request) =>
        request?.Headers.Authorization.ToString().StartsWith("Bearer aws_", StringComparison.Ordinal) == true;

    /// <summary>
    /// Keeps an embedded session inside its survey: the account, its own session, that survey's endpoints and the
    /// shared ones the designer reads (themes, templates, assets, messages); everything else is 403.
    /// </summary>
    public static IApplicationBuilder UseEmbedLimits(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.FindFirst(SurveyClaim)?.Value is { } survey && !Allowed(context.Request.Method, context.Request.Path.Value ?? "", survey))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });

    private static bool Allowed(string method, string path, string survey)
    {
        if (!path.StartsWith("/api/app/", StringComparison.Ordinal))
        {
            return true;
        }

        var rest = path["/api/app/".Length..];
        var first = rest.Split('/')[0];

        // The survey list of a host app (W37, <askwerk-surveys>): the list and the shared reads, nothing else.
        if (survey == ListSurvey)
        {
            return first is "me" or "embed" or "themes" or "assets" or "telemetry" or "status" || (rest.TrimEnd('/') == "surveys" && HttpMethods.IsGet(method));
        }

        return first switch
        {
            "me" or "embed" or "themes" or "templates" or "assets" or "messages" or "telemetry" or "status" or "exports" => true,
            "surveys" or "run" => rest.Split('/') is [_, var id, ..] && string.Equals(id, survey, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}

/// <summary>Authenticates the bearer token of an embedded session: its user, organisation and survey.</summary>
public sealed class EmbedSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider services) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!EmbedSessions.HasToken(Request))
        {
            return AuthenticateResult.NoResult();
        }

        var token = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var database = services.GetRequiredService<AskwerkDbContext>();
        var hash = EmbedSession.HashOf(token);
        var found = await database.EmbedSessions.FromSqlInterpolated($"SELECT * FROM embed_session_lookup({hash})").AsNoTracking().FirstOrDefaultAsync(Context.RequestAborted);

        if (found is null)
        {
            return AuthenticateResult.Fail("Unknown or expired embedded session.");
        }

        var session = services.GetRequiredService<DbSessionContext>();
        session.TenantId = found.TenantId;
        session.UserId = found.UserId;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, found.UserId.ToString()),
            new(Sessions.TenantClaim, found.TenantId.ToString()),
            new(EmbedSessions.SurveyClaim, found.SurveyId.ToString()),
        };

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}

/// <summary>Starts and renews embedded sessions (AWK-13, W21).</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Route("api/app/embed/session")]
public sealed class EmbedSessionController(
    AskwerkDbContext context,
    DbSessionContext session,
    HandoverKeys keys,
    ExternalUsers externalUsers,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    /// <summary>Starts an embedded session from the host's hand-over token for one survey.</summary>
    /// <param name="request">The token and what to show.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session's token, its expiry and end; 401 token-invalid; 403 embedding-off or no-access; 404 survey-not-found.</returns>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<EmbedSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<EmbedProblem>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<EmbedProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<EmbedProblem>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(EmbedSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = EmbedSessions.Views.Contains(request.View) ? request.View! : "designer";

        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 16_000 || HandoverTokens.TenantOf(request.Token) is not { } tenantId)
        {
            return Problem(StatusCodes.Status401Unauthorized, "token-invalid");
        }

        session.TenantId = tenantId;
        await context.Database.CloseConnectionAsync();
        var origins = await context.Database.SqlQuery<string[]>($"SELECT designer_embed_origins({tenantId}) AS \"Value\"").SingleOrDefaultAsync(cancellationToken);

        if (origins is not { Length: > 0 })
        {
            return Problem(StatusCodes.Status403Forbidden, "embedding-off");
        }

        var settings = await context.TenantSignIns.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);
        var now = time.GetUtcNow();

        if (settings is not { HandoverEnabled: true, HandoverIssuer: { } issuer }
            || HandoverTokens.Check(request.Token, tenantId, issuer, await keys.OfAsync(settings, cancellationToken), now) is not { } claims
            || await context.HandoverTokenUses.AnyAsync(row => row.TenantId == tenantId && row.Jti == claims.Jti, cancellationToken))
        {
            return Problem(StatusCodes.Status401Unauthorized, "token-invalid");
        }

        context.HandoverTokenUses.Add(new HandoverTokenUse(tenantId, claims.Jti, claims.ExpiresAt));

        // The survey list needs no survey; every other view opens the one in the token.
        var surveyId = view == "list" ? Guid.Empty : claims.Survey ?? Guid.Empty;

        if (view != "list" && (surveyId == Guid.Empty || !await context.Surveys.AnyAsync(survey => survey.Id == surveyId && survey.DeletedAt == null, cancellationToken)))
        {
            await context.SaveChangesAsync(cancellationToken);
            return Problem(StatusCodes.Status404NotFound, "survey-not-found");
        }

        var deactivated = await context.ExternalIdentities.AnyAsync(row => row.TenantId == tenantId && row.ExternalId == claims.Subject && !row.Active, cancellationToken);

        if (deactivated || settings.RoleFor(claims.Groups) is not { } role)
        {
            await context.SaveChangesAsync(cancellationToken);
            return Problem(StatusCodes.Status403Forbidden, "no-access");
        }

        var result = await externalUsers.ApplyAsync(tenantId, claims.Subject, claims.Email, claims.Name, role, "handover", null, cancellationToken);
        session.UserId = result.User.Id;
        var started = EmbedSession.Start(Guid.CreateVersion7(now), tenantId, result.User.Id, surveyId, view is "designer" && result.Role is TenantRole.Analyst or TenantRole.Viewer ? "results" : view, now);
        context.EmbedSessions.Add(started.Session);
        await audit.WriteAsync(new AuditContent(now, tenantId, AuditActorKind.User, result.User.Id.ToString(), "embed.session-started", "Survey", surveyId.ToString(), JsonSerializer.Serialize(new { view = started.Session.View, externalId = claims.Subject }), trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var hostLook = await context.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.HostLook).SingleOrDefaultAsync(cancellationToken);

        return Ok(new EmbedSessionResponse(started.Token, started.Session.ExpiresAt, started.Session.EndsAt, surveyId, started.Session.View, claims.Locale, Askwerk.Api.App.Domains.HostLook.Read(hostLook)));
    }

    /// <summary>Renews the embedded session's token for another hour, never past its end.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new expiry; 401 when the session has expired.</returns>
    [HttpPost("renew")]
    [Authorize(AuthenticationSchemes = EmbedSessions.Scheme)]
    [ProducesResponseType<EmbedRenewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Renew(CancellationToken cancellationToken)
    {
        var hash = EmbedSession.HashOf(Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim());
        var found = await context.EmbedSessions.FirstOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);

        if (found is null)
        {
            return Unauthorized();
        }

        found.Renew(time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);

        return Ok(new EmbedRenewResponse(found.ExpiresAt, found.EndsAt));
    }

    private ObjectResult Problem(int status, string code) => StatusCode(status, new EmbedProblem(code));
}

/// <summary>An embedded session to start.</summary>
/// <param name="Token">The host's hand-over token, with the claim survey.</param>
/// <param name="View">designer (the default) or results.</param>
public sealed record EmbedSessionRequest(string? Token, string? View);

/// <summary>A started embedded session.</summary>
/// <param name="Token">The bearer token (aws_…); keep it in memory only.</param>
/// <param name="ExpiresAt">When it stops working unless renewed.</param>
/// <param name="EndsAt">When the session ends for good.</param>
/// <param name="SurveyId">The survey it may open.</param>
/// <param name="View">designer or results; analysts and viewers always get results.</param>
/// <param name="Language">The token's locale, if any.</param>
/// <param name="HostLook">The organisation's host look (W37), which the frame follows unless the tag gives its own.</param>
public sealed record EmbedSessionResponse(string Token, DateTimeOffset ExpiresAt, DateTimeOffset EndsAt, Guid SurveyId, string View, string? Language, Askwerk.Api.App.Domains.HostLook? HostLook = null);

/// <summary>A renewed embedded session.</summary>
/// <param name="ExpiresAt">When it stops working unless renewed again.</param>
/// <param name="EndsAt">When the session ends for good.</param>
public sealed record EmbedRenewResponse(DateTimeOffset ExpiresAt, DateTimeOffset EndsAt);

/// <summary>Why an embedded session did not start: token-invalid, embedding-off, no-access or survey-not-found.</summary>
/// <param name="Code">The code.</param>
public sealed record EmbedProblem(string Code);

/// <summary>Who may frame the embedded designer: the organisation the token names lists the websites.</summary>
internal static class EmbedFrames
{
    /// <summary>The frame-ancestors policy of an /embed page, from the token in its address.</summary>
    public static async Task<string> PolicyAsync(HttpContext http)
    {
        var token = http.Request.Query["token"].ToString();

        if (token.Length is 0 or > 16_000 || HandoverTokens.TenantOf(token) is not { } tenantId || !http.RequestServices.GetRequiredService<DatabaseState>().IsReady)
        {
            return "frame-ancestors 'none'";
        }

        var context = http.RequestServices.GetRequiredService<AskwerkDbContext>();
        var origins = await context.Database.SqlQuery<string[]>($"SELECT designer_embed_origins({tenantId}) AS \"Value\"").SingleOrDefaultAsync(http.RequestAborted);

        return origins is { Length: > 0 } ? string.Join(' ', ["frame-ancestors", .. origins]) : "frame-ancestors 'none'";
    }
}
