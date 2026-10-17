// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Api.Startup;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Domains;

/// <summary>
/// The websites that may show the organisation's surveys in a frame (AWK-7, W12 B note 5). Embedding is off until an
/// owner or admin lists one; survey pages then allow exactly these websites as frame ancestors.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/embedding")]
public sealed class EmbeddingController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private const int MaxOrigins = 20;

    /// <summary>The websites that may embed the surveys.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The origins, e.g. https://www.acme.at.</returns>
    [HttpGet]
    [ProducesResponseType<EmbeddingResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmbeddingResponse>> Get(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();
        var tenant = await context.Tenants.AsNoTracking().Where(tenant => tenant.Id == tenantId).Select(tenant => new { tenant.EmbedOrigins, tenant.DesignerEmbedOrigins, tenant.HostLook }).SingleAsync(cancellationToken);

        return new EmbeddingResponse(tenant.EmbedOrigins, tenant.DesignerEmbedOrigins, HostLook.Read(tenant.HostLook));
    }

    /// <summary>
    /// The host look (W37): accent, background, font and corners that drop-in components follow when the tag gives
    /// none. All empty clears it.
    /// </summary>
    /// <param name="request">The four values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The look as kept; 400 look-invalid; 403 for other roles.</returns>
    [HttpPut("host-look")]
    [ProducesResponseType<HostLook>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PutHostLook(HostLook request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is not (TenantRole.Owner or TenantRole.Admin))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new DomainProblem("role-not-allowed", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var (look, valid) = request.Checked();

        if (!valid)
        {
            ModelState.AddModelError("look", "look-invalid");
            return ValidationProblem(ModelState);
        }

        var tenant = await context.Tenants.SingleAsync(row => row.Id == tenantId, cancellationToken);
        tenant.SetHostLook(look is null ? null : JsonSerializer.Serialize(look, JsonSerializerOptions.Web));
        await audit.WriteAsync(new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, userId.ToString(), "host-look.changed", "Tenant", tenant.Id.ToString(), tenant.HostLook, trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(look ?? new HostLook(null, null, null, null));
    }

    /// <summary>Replaces the websites that may embed the surveys; an empty list switches embedding off.</summary>
    /// <param name="request">The origins: scheme, host and port, e.g. https://www.acme.at.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 with origins: origin-invalid or too-many-origins; 403 for other roles.</returns>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<DomainProblem>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Put(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = User.TenantId();
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is not (TenantRole.Owner or TenantRole.Admin))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new DomainProblem("role-not-allowed", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var tenant = await context.Tenants.SingleAsync(row => row.Id == tenantId, cancellationToken);

        if (request.Origins is not null)
        {
            if (Origins(request.Origins, "origins") is not { } origins)
            {
                return ValidationProblem(ModelState);
            }

            tenant.SetEmbedOrigins(origins);
        }

        if (request.DesignerOrigins is not null)
        {
            if (Origins(request.DesignerOrigins, "designerOrigins") is not { } designer)
            {
                return ValidationProblem(ModelState);
            }

            tenant.SetDesignerEmbedOrigins(designer);
        }

        await audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, userId.ToString(), "embedding.changed", "Tenant", tenant.Id.ToString(), JsonSerializer.Serialize(new { origins = tenant.EmbedOrigins, designerOrigins = tenant.DesignerEmbedOrigins }), trace.CurrentTraceId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>A website's origin as browsers send it: http(s), host, port only when not the default; null otherwise.</summary>
    private List<string>? Origins(IReadOnlyList<string>? given, string field)
    {
        var origins = new List<string>();

        foreach (var each in given ?? [])
        {
            if (Origin(each) is not { } origin)
            {
                ModelState.AddModelError(field, "origin-invalid");
                return null;
            }

            if (!origins.Contains(origin))
            {
                origins.Add(origin);
            }
        }

        if (origins.Count > MaxOrigins)
        {
            ModelState.AddModelError(field, "too-many-origins");
            return null;
        }

        return origins;
    }

    internal static string? Origin(string? given)
    {
        if (!Uri.TryCreate(given?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0
            || uri.Host.Contains('*', StringComparison.Ordinal) || !uri.Host.Contains('.', StringComparison.Ordinal) && uri.Host != "localhost")
        {
            return null;
        }

        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}

/// <summary>
/// Survey pages say who may frame them (Content-Security-Policy frame-ancestors): Askwerk itself, plus the
/// organisation's listed websites. Every other page of the app is framed by Askwerk only.
/// </summary>
internal static class FrameAncestors
{
    public static IApplicationBuilder UseFrameAncestors(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            var path = http.Request.Path.Value ?? "";

            if (path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/health", StringComparison.Ordinal) || path.Contains('.', StringComparison.Ordinal))
            {
                await next(http);
                return;
            }

            if (path.StartsWith("/embed/", StringComparison.Ordinal))
            {
                http.Response.Headers.ContentSecurityPolicy = await EmbedFrames.PolicyAsync(http);
                await next(http);
                return;
            }

            var policy = "frame-ancestors 'self'";
            var segment = path.StartsWith("/s/", StringComparison.Ordinal) ? path[3..].Split('/')[0] : "";

            if (segment.Length is > 0 and <= 64 && http.RequestServices.GetRequiredService<DatabaseState>().IsReady)
            {
                var context = http.RequestServices.GetRequiredService<AskwerkDbContext>();
                var origins = await context.Database.SqlQuery<string[]>($"SELECT run_embed_origins({segment.ToLowerInvariant()}) AS \"Value\"").SingleAsync(http.RequestAborted);
                policy = string.Join(' ', [policy, .. origins ?? []]);
            }

            http.Response.Headers.ContentSecurityPolicy = policy;
            await next(http);
        });
}

/// <summary>The websites that may embed the surveys.</summary>
/// <param name="Origins">E.g. https://www.acme.at.</param>
/// <param name="DesignerOrigins">Websites that may show the designer and Results (AWK-13); none: off.</param>
/// <param name="HostLook">What drop-in components follow (W37); null when not set.</param>
public sealed record EmbeddingResponse(IReadOnlyList<string> Origins, IReadOnlyList<string> DesignerOrigins, HostLook? HostLook = null);

/// <summary>The websites to allow.</summary>
/// <param name="Origins">Scheme, host and port, e.g. https://www.acme.at; at most 20; null keeps them.</param>
/// <param name="DesignerOrigins">Websites that may show the designer and Results; null keeps them, empty switches it off.</param>
public sealed record EmbeddingRequest(IReadOnlyList<string>? Origins, IReadOnlyList<string>? DesignerOrigins = null);

/// <summary>
/// The host look (W37): what drop-in components follow in a host app. Only safe values: colours (#hex, rgb(), hsl()),
/// a plain font list, corners of 0 to 24 px.
/// </summary>
/// <param name="Accent">Buttons, links, the key result's bar.</param>
/// <param name="Background">Behind the components.</param>
/// <param name="Font">The font list, e.g. "Segoe UI", sans-serif.</param>
/// <param name="Radius">Corners in px.</param>
public sealed partial record HostLook(string? Accent, string? Background, string? Font, int? Radius)
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|rgba?\([\d\s.,%]+\)|hsla?\([\d\s.,%deg]+\))$")]
    private static partial System.Text.RegularExpressions.Regex Colour();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[\w\s""',.-]{1,200}$")]
    private static partial System.Text.RegularExpressions.Regex FontList();

    /// <summary>The look trimmed and checked; null when all is empty.</summary>
    /// <returns>The look, and whether every value given is allowed.</returns>
    public (HostLook? Look, bool Valid) Checked()
    {
        var accent = string.IsNullOrWhiteSpace(Accent) ? null : Accent.Trim();
        var background = string.IsNullOrWhiteSpace(Background) ? null : Background.Trim();
        var font = string.IsNullOrWhiteSpace(Font) ? null : Font.Trim();
        var valid = (accent is null || Colour().IsMatch(accent)) && (background is null || Colour().IsMatch(background)) && (font is null || FontList().IsMatch(font)) && Radius is null or (>= 0 and <= 24);

        return (accent, background, font, Radius) is (null, null, null, null) ? (null, valid) : (new HostLook(accent, background, font, Radius), valid);
    }

    /// <summary>The kept look, or null.</summary>
    /// <param name="json">The tenant's host_look.</param>
    /// <returns>The look.</returns>
    public static HostLook? Read(string? json) => json is null ? null : JsonSerializer.Deserialize<HostLook>(json, JsonSerializerOptions.Web);
}
