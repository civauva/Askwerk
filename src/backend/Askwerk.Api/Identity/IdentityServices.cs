// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Security.Claims;
using Askwerk.Api.Configuration;
using Askwerk.Application.Setup;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Options;

namespace Askwerk.Api.Identity;

/// <summary>Wires ASP.NET Core Identity, the session cookie, the authorisation policies and row-level security per request.</summary>
public static class IdentityServices
{
    /// <summary>Adds identity, sessions and the setup use case.</summary>
    /// <param name="services">The services.</param>
    /// <param name="settings">Askwerk's start settings.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddAskwerkIdentity(this IServiceCollection services, AskwerkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Keys for cookies and tokens survive restarts in the data directory (a volume).
        services.AddDataProtection()
            .SetApplicationName("askwerk")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settings.DataDirectory, "keys")));

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = Sessions.CookieName;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.SlidingExpiration = true;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);

            // An API answers with status codes, never with redirects to a login page.
            cookie.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddIdentityCore<User>(options =>
            {
                // Length over composition rules (NIST SP 800-63B): at least 12 characters, nothing else forced.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                // Approved by Dražan, 2026-10-13: 5 failed tries, then a 15-minute pause.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddUserStore<UserStore>()
            .AddSignInManager()

            // Authenticator codes (two-factor) and signed tokens (password reset); no SMS or phone tokens.
            .AddTokenProvider<AuthenticatorTokenProvider<User>>(TokenOptions.DefaultAuthenticatorProvider)
            .AddTokenProvider<DataProtectorTokenProvider<User>>(TokenOptions.DefaultProvider);

        // Reset links work for 30 minutes (approved 2026-10-13) and only once: resetting changes the security stamp.
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMinutes(30));

        // Every request checks only that the session's security stamp is still the user's current one (AWK-21): cheap,
        // cached, and the cookie is not rewritten. A password change, "sign out everywhere" or a two-factor change sets a
        // new stamp, so other sessions end on their next request. The database session learns the user first, because
        // row-level security shows a user only their own row.
        services.AddMemoryCache();
        services.AddSingleton<SecurityStamps>();
        services.AddSingleton<TenantStates>();
        services.ConfigureApplicationCookie(options => options.Events.OnValidatePrincipal = async context =>
        {
            var services = context.HttpContext.RequestServices;
            var session = services.GetRequiredService<DbSessionContext>();
            var userId = context.Principal?.UserId();
            session.UserId = userId;
            session.TenantId = context.Principal?.TenantId();
            var claimType = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
            var stamp = context.Principal?.FindFirstValue(claimType);
            var current = userId is null
                ? null
                : await services.GetRequiredService<SecurityStamps>().CurrentAsync(userId.Value, services.GetRequiredService<AskwerkDbContext>(), context.HttpContext.RequestAborted);

            if (current is null || stamp != current)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }

            // A suspended or deleted organisation (AWK-38) is as if none were chosen: the session stays, its data is closed.
            if (session.TenantId is { } tenantId
                && !await services.GetRequiredService<TenantStates>().IsOpenAsync(tenantId, services.GetRequiredService<AskwerkDbContext>(), context.HttpContext.RequestAborted))
            {
                session.TenantId = null;
                var identity = new ClaimsIdentity(context.Principal!.Claims.Where(claim => claim.Type != Sessions.TenantClaim), context.Principal.Identity?.AuthenticationType);
                context.ReplacePrincipal(new ClaimsPrincipal(identity));
            }
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Sessions.SignedIn, policy => policy.RequireAuthenticatedUser().RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == Sessions.PendingClaim)))
            .AddPolicy(Sessions.SignedInPendingAllowed, policy => policy.RequireAuthenticatedUser());

        services.AddScoped<DbSessionContext>();
        services.AddScoped<IDataScope, DbDataScope>();
        services.AddScoped<EfUserRepository>();
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<EfInstanceSettingsRepository>();
        services.AddSingleton(settings.Overrides ?? new InstallationOverrides());
        services.AddScoped<IInstanceSettingsRepository, ConfiguredInstanceSettingsRepository>();
        services.AddScoped<IAccounts, IdentityAccounts>();
        services.AddScoped<SessionService>();
        services.AddScoped<ExternalUsers>();
        services.AddScoped<DirectoryPasswords>();
        services.AddSingleton<HandoverKeys>();
        services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>, OidcOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>, Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectPostConfigureOptions>();
        services.AddHttpClient(HandoverKeys.Client, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<AccountAudit>();
        services.AddScoped<ISecretProtector, DataProtectionSecrets>();
        services.AddScoped<EmailSettingsProvider>();
        services.AddSingleton<Askwerk.Application.Email.IEmailSender, SmtpEmailSender>();
        services.AddScoped<CompleteSetupHandler>();
        services.AddExceptionHandler<DomainValidationExceptionHandler>();

        return services;
    }

    /// <summary>Puts the signed-in user and their tenant on the request's database session (row-level security).</summary>
    /// <param name="app">The app.</param>
    /// <returns>The app.</returns>
    public static IApplicationBuilder UseAskwerkSessionScope(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var session = context.RequestServices.GetRequiredService<DbSessionContext>();
            session.UserId = context.User.UserId();
            session.TenantId = context.User.TenantId();
            await next(context);
        });
}

/// <summary>Turns a broken domain rule into a 400 problem at the field: <c>{ "errors": { "slug": ["slug-taken"] } }</c>.</summary>
public sealed class DomainValidationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not DomainValidationException validation)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { [validation.Field] = [validation.Code] })
        {
            Status = StatusCodes.Status400BadRequest,
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString();

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
