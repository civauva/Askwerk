// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Askwerk.Api.Identity;

/// <summary>
/// Single sign-on with each organisation's OpenID Connect provider (AWK-12, W22 step 2): one authentication scheme per
/// organisation, added when first used and configured from its settings; authorization code flow with PKCE.
/// </summary>
public static class Oidc
{
    private const string Prefix = "oidc-";

    /// <summary>The scheme of an organisation.</summary>
    public static string SchemeOf(Guid tenantId) => Prefix + tenantId.ToString("N");

    /// <summary>The organisation of a scheme, if it is one of these.</summary>
    public static Guid? TenantOfScheme(string? scheme) =>
        scheme is not null && scheme.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(scheme[Prefix.Length..], "N", out var tenant) ? tenant : null;

    /// <summary>Where the provider sends the browser back; registered at the provider as the redirect URI.</summary>
    public static string CallbackOf(Guid tenantId) => $"/sso/callback/{tenantId:N}";

    /// <summary>Adds the organisation's scheme if it is not there yet (after a restart, or on first use).</summary>
    public static async Task EnsureSchemeAsync(IAuthenticationSchemeProvider schemes, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        var name = SchemeOf(tenantId);

        if (await schemes.GetSchemeAsync(name) is null)
        {
            schemes.TryAddScheme(new AuthenticationScheme(name, null, typeof(OpenIdConnectHandler)));
        }
    }

    /// <summary>Forgets an organisation's options, so the next sign-in reads its changed settings.</summary>
    public static void Forget(IOptionsMonitorCache<OpenIdConnectOptions> cache, Guid tenantId) => cache?.TryRemove(SchemeOf(tenantId));

    /// <summary>Lets a provider's answer reach its scheme even when the scheme was not added since the start.</summary>
    public static IApplicationBuilder UseOidcCallbacks(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;

            if (path is not null && path.StartsWith("/sso/callback/", StringComparison.Ordinal) && Guid.TryParseExact(path["/sso/callback/".Length..], "N", out var tenant))
            {
                await EnsureSchemeAsync(context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>(), tenant);
            }

            await next();
        });

    /// <summary>The first value of a claim of the provider's ID token.</summary>
    internal static string? Claim(System.Security.Claims.ClaimsPrincipal? principal, string type) => principal?.FindFirst(type)?.Value;
}

/// <summary>Configures each organisation's scheme from its settings in the database.</summary>
public sealed class OidcOptionsSetup(IServiceScopeFactory scopes) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    /// <inheritdoc/>
    public void Configure(OpenIdConnectOptions options)
    {
    }

    /// <inheritdoc/>
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (Oidc.TenantOfScheme(name) is not { } tenantId)
        {
            return;
        }

        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<DbSessionContext>().TenantId = tenantId;
        var settings = scope.ServiceProvider.GetRequiredService<AskwerkDbContext>().TenantSignIns.AsNoTracking().FirstOrDefault(row => row.TenantId == tenantId);
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        // A scheme without settings still needs a valid shape; /sso refuses before it is ever challenged.
        options.Authority = settings?.OidcAuthority ?? "https://sso.invalid";
        options.ClientId = settings?.OidcClientId ?? "none";
        options.ClientSecret = settings?.OidcClientSecretProtected is { } secret ? secrets.Unprotect(secret) : null;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.CallbackPath = Oidc.CallbackOf(tenantId);
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnTicketReceived = OidcSignIn.TicketReceivedAsync;
        options.Events.OnRemoteFailure = OidcSignIn.RemoteFailureAsync;
    }
}

/// <summary>What happens when the provider answers: the person becomes a member with the mapped role and is signed in.</summary>
public static class OidcSignIn
{
    /// <summary>Binds the provider's user (matched by email the first time), maps the role, starts the session.</summary>
    public static async Task TicketReceivedAsync(TicketReceivedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HandleResponse();
        var http = context.HttpContext;
        var services = http.RequestServices;
        var cancellationToken = http.RequestAborted;
        var tenantId = Oidc.TenantOfScheme(context.Scheme.Name)!.Value;
        var principal = context.Principal;
        var subject = Oidc.Claim(principal, "sub");
        var email = Oidc.Claim(principal, "email");

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || Oidc.Claim(principal, "email_verified") == "false")
        {
            await SignInFailure.WriteAsync(http, "invalid", null);
            return;
        }

        var session = services.GetRequiredService<DbSessionContext>();
        var db = services.GetRequiredService<AskwerkDbContext>();
        session.TenantId = tenantId;
        await db.Database.CloseConnectionAsync();
        var settings = await db.TenantSignIns.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);
        var groups = principal!.FindAll("groups").Concat(principal.FindAll("roles")).Select(claim => claim.Value).ToList();
        var externalId = "oidc|" + subject;
        var deactivated = await db.ExternalIdentities.AnyAsync(row => row.TenantId == tenantId && row.ExternalId == externalId && !row.Active, cancellationToken);

        if (settings is not { OidcEnabled: true } || deactivated || settings.RoleFor(groups) is not { } role)
        {
            await SignInFailure.WriteAsync(http, "no-access", await db.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.Name).FirstOrDefaultAsync(cancellationToken));
            return;
        }

        ExternalUserResult result;

        try
        {
            result = await services.GetRequiredService<ExternalUsers>().ApplyAsync(tenantId, externalId, email, Oidc.Claim(principal, "name") ?? email, role, "oidc", null, cancellationToken);
        }
        catch (DomainValidationException)
        {
            await SignInFailure.WriteAsync(http, "invalid", null);
            return;
        }

        session.UserId = result.User.Id;
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        await services.GetRequiredService<SessionService>().SignInAsync(result.User, tenantId, cancellationToken, external: true);
        http.Response.Redirect(SignInFailure.SafeReturn(context.ReturnUri));
    }

    /// <summary>The provider refused or the answer did not check out: the same page as a broken hand-over.</summary>
    public static async Task RemoteFailureAsync(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HandleResponse();
        await SignInFailure.WriteAsync(context.HttpContext, "invalid", null);
    }
}

/// <summary>The small page shown when an external sign-in cannot go on, in the browser's language.</summary>
public static class SignInFailure
{
    /// <summary>A path inside Askwerk to open after signing in, or the start page.</summary>
    public static string SafeReturn(string? path) =>
        path is not null && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal) ? path : "/";

    /// <summary>The page: "invalid" (link no longer works), "no-access", or "not-set-up".</summary>
    public static (int Status, string Html) Page(HttpRequest request, string reason, string? organisation)
    {
        ArgumentNullException.ThrowIfNull(request);
        var german = request.Headers.AcceptLanguage.ToString().StartsWith("de", StringComparison.OrdinalIgnoreCase);
        var (title, text) = (reason, german) switch
        {
            ("no-access", true) => ("Kein Zugang", $"Ihr Konto hat keinen Zugang zu {organisation}. Wenden Sie sich an Ihre Administration."),
            ("no-access", false) => ("No access", $"Your account has no access to {organisation}. Ask your administrator."),
            ("not-set-up", true) => ("Keine Einmalanmeldung", "Für diese Organisation ist keine Einmalanmeldung eingerichtet. Melden Sie sich mit Ihrem Askwerk-Konto an."),
            ("not-set-up", false) => ("No single sign-on", "Single sign-on is not set up for this organisation. Sign in with your Askwerk account."),
            (_, true) => ("Anmeldung nicht möglich", "Dieser Anmeldelink gilt nicht mehr. Starten Sie erneut in Ihrem Produkt."),
            _ => ("Cannot sign in", "This sign-in link no longer works. Start again from your product."),
        };
        var html = $"""
            <!doctype html><html lang="{(german ? "de" : "en")}"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)} · Askwerk</title>
            <body style="font-family:system-ui,sans-serif;max-width:32rem;margin:15vh auto;padding:0 1rem;line-height:1.5">
            <h1 style="font-size:1.25rem">{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(text)}</p><p><a href="/">Askwerk</a></p></body></html>
            """;

        return (reason switch { "no-access" => StatusCodes.Status403Forbidden, "not-set-up" => StatusCodes.Status404NotFound, _ => StatusCodes.Status401Unauthorized }, html);
    }

    /// <summary>Writes the page as the response.</summary>
    public static async Task WriteAsync(HttpContext context, string reason, string? organisation)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (status, html) = Page(context.Request, reason, organisation);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted);
    }

    /// <summary>The page as an action result.</summary>
    public static ContentResult Result(HttpRequest request, string reason, string? organisation)
    {
        var (status, html) = Page(request, reason, organisation);

        return new ContentResult { StatusCode = status, ContentType = "text/html; charset=utf-8", Content = html };
    }
}

/// <summary>Starts single sign-on: /sso/{organisation short name}, optionally ?return=/path. The link owners share.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("sso")]
public sealed class SsoController(AskwerkDbContext context, IAuthenticationSchemeProvider schemes) : ControllerBase
{
    /// <summary>Sends the browser to the organisation's provider.</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Start(string slug, [FromQuery(Name = "return")] string? returnTo, CancellationToken cancellationToken)
    {
        var tenantId = slug.Length > 40 ? null : await context.Database.SqlQuery<Guid?>($"SELECT oidc_tenant_by_slug({slug.ToLowerInvariant()}) AS \"Value\"").FirstOrDefaultAsync(cancellationToken);

        if (tenantId is not { } tenant)
        {
            var directory = slug.Length > 40 ? false : await context.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM sso_options({slug.ToLowerInvariant()}) WHERE ldap) AS \"Value\"").SingleAsync(cancellationToken);

            return directory ? Redirect($"/?sso={Uri.EscapeDataString(slug.ToLowerInvariant())}") : SignInFailure.Result(Request, "not-set-up", null);
        }

        await Oidc.EnsureSchemeAsync(schemes, tenant);

        return Challenge(new AuthenticationProperties { RedirectUri = SignInFailure.SafeReturn(returnTo) }, Oidc.SchemeOf(tenant));
    }
}
