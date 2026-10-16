// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Integrations;
using Askwerk.Api.App.Domains;
using Askwerk.Application.Storage;
using Askwerk.Infrastructure.Storage;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Askwerk.Api.App.Telemetry;
using Askwerk.Api.Configuration;
using Askwerk.Api.Identity;
using Askwerk.Api.Startup;
using Askwerk.Api.Telemetry;
using Askwerk.Application.Auditing;
using Askwerk.Application.Exports;
using Askwerk.Application.Instances;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var settings = AskwerkSettings.From(builder.Configuration);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DatabaseState>();
builder.Services.AddHostedService<DatabaseStartupService>();

// Persistence: one context per request, connected as the restricted app role once first start is done.
builder.Services.AddScoped(services => AskwerkDbContext.Create(
    services.GetRequiredService<DatabaseState>().RequireAppConnectionString(),
    services.GetRequiredService<DbSessionContext>()));
builder.Services.AddScoped<IInstanceRepository, EfInstanceRepository>();
builder.Services.AddScoped<IAuditWriter, EfAuditWriter>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<AuditLogReader>();
builder.Services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

// Domains (AWK-7): our own DNS client, and the checks that run by themselves.
builder.Services.AddSingleton<Askwerk.Application.Domains.IDnsLookup, Askwerk.Infrastructure.Dns.DnsLookup>();
builder.Services.AddSingleton<Askwerk.Api.App.Domains.DomainChecks>();
builder.Services.AddHostedService<Askwerk.Api.App.Domains.DomainChecksService>();

// Removing deleted organisations' data after 30 days (AWK-39).
builder.Services.AddSingleton<Askwerk.Api.App.Installation.TenantRemoval>();
builder.Services.AddHostedService<Askwerk.Api.App.Installation.TenantRemovalService>();

// Background jobs (AWK-7): the job table, run every 15 seconds; invitations and reminders are the first kinds.
builder.Services.AddSingleton<Askwerk.Api.App.Jobs.JobRunner>();
builder.Services.AddHostedService<Askwerk.Api.App.Jobs.JobService>();
builder.Services.AddScoped<Askwerk.Api.App.Jobs.IJobHandler, Askwerk.Api.App.Participants.SendInvitationsJob>();
builder.Services.AddScoped<Askwerk.Api.App.Jobs.IJobHandler, Askwerk.Api.App.Participants.SendRemindersJob>();

// Result exports (AWK-24): one exporter per format.
builder.Services.AddSingleton<IExporter, XlsxExporter>();
builder.Services.AddSingleton<IExporter, CsvExporter>();
builder.Services.AddSingleton<IExporter, SpssExporter>();
builder.Services.AddSingleton<IExporter, StataExporter>();
builder.Services.AddSingleton<IExporter, JsonExporter>();

// Exports as jobs (AWK-25): prepared in the background, kept 7 days, then deleted.
builder.Services.AddScoped<Askwerk.Api.App.Exports.ExportWriter>();
builder.Services.AddScoped<Askwerk.Api.App.Exports.ExportService>();
builder.Services.AddScoped<Askwerk.Api.App.Jobs.IJobHandler, Askwerk.Api.App.Exports.PrepareExportJob>();
builder.Services.AddScoped<Askwerk.Api.App.Jobs.IJobHandler, Askwerk.Api.App.Exports.DeleteExportJob>();
builder.Services.AddScoped<Askwerk.Api.Integrations.WebhookService>();
builder.Services.AddScoped<Askwerk.Api.App.Jobs.IJobHandler, Askwerk.Api.Integrations.DeliverWebhookJob>();
builder.Services.AddScoped<Askwerk.Api.App.Jobs.IJobHandler, Askwerk.Api.App.Surveys.CloseSurveyJob>();
builder.Services.AddHttpClient(Askwerk.Api.Integrations.DeliverWebhookJob.Client, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IFileStorage>(new FolderFileStorage(Path.Combine(settings.DataDirectory, "assets")));
builder.Services.AddSingleton<ITraceContext, ActivityTraceContext>();
builder.Services.AddAskwerkIdentity(settings);

// The Public API (AWK-25): API keys on /api/v1 only, the cookie session everywhere else.
builder.Services.AddPublicApi();

// Telemetry: .NET abstractions in code, OpenTelemetry for export (agents/observability.md).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddSingleton<FrontendMetrics>();
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("askwerk-api", serviceVersion: settings.Version))
    .WithTracing(tracing => tracing
        .AddSource("Askwerk.*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .WithMetrics(metrics => metrics
        .AddMeter("Askwerk.*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsqlInstrumentation());
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    telemetry.UseOtlpExporter();
}

// HTTP surface: controllers only, problem details with the trace id on every error.
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString());
builder.Services.AddHealthChecks().AddCheck<DatabaseReadyHealthCheck>("database", tags: ["ready"]);

// The built apps go out compressed (AWK-21). API answers stay uncompressed: compressing secrets next to
// attacker-chosen text over TLS leaks them through the size (BREACH).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = ["text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml", "application/manifest+json"];
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(TelemetryController.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
{
    // The committed contract must not depend on the host it was generated on.
    document.Info = new()
    {
        Title = "Askwerk Public API",
        Version = "v1",
        Description = "A stable subset of Askwerk for integrators. Authenticate with an API key from Settings → API as Authorization: Bearer <key>. Errors are RFC 7807 problem details; see docs/api/errors.md.",
    };
    document.Servers = [];
    document.Components ??= new();
    document.Components.SecuritySchemes = new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>
    {
        [PublicApi.Scheme] = new Microsoft.OpenApi.OpenApiSecurityScheme { Type = Microsoft.OpenApi.SecuritySchemeType.Http, Scheme = "bearer", Description = "An API key from Settings → API." },
    };
    document.Security = [new Microsoft.OpenApi.OpenApiSecurityRequirement { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference(PublicApi.Scheme, document)] = [] }];

    return Task.CompletedTask;
}));
builder.Services.AddOpenApi("app", options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new() { Title = "Askwerk App API (internal, unversioned)", Version = "app" };
    document.Servers = [];

    return Task.CompletedTask;
}));

var app = builder.Build();

// Registered IExceptionHandlers (DomainValidationExceptionHandler) run first; this handles the rest.
app.UseExceptionHandler(errors => errors.Run(async context =>
{
    var problems = context.RequestServices.GetRequiredService<IProblemDetailsService>();
    var notReady = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error is DatabaseNotReadyException;
    context.Response.StatusCode = notReady ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;

    await problems.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = new ProblemDetails { Status = context.Response.StatusCode },
    });
}));
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseMiddleware<PublicApiRateLimit>();
app.UseOidcCallbacks();
app.UseAuthentication();
app.UseEmbedLimits();
app.UseAskwerkSessionScope();
app.UseAuthorization();

// The built designer app (src/frontend/apps/designer) and the runner (apps/runner, in s/) are served by the same process: one container.
var appFiles = Directory.Exists(settings.WebRoot) ? new PhysicalFileProvider(settings.WebRoot) : null;

// Vite names built files after their content (assets/…-hash.js), so they can be cached for a year; pages are
// always checked again, so a new release shows at once (AWK-21).
var appFileOptions = new StaticFileOptions
{
    FileProvider = appFiles,
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
        context.Context.Request.Path.StartsWithSegments("/assets") || context.Context.Request.Path.StartsWithSegments("/s/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache",
};

// Who may frame Askwerk's pages: survey pages also the organisation's listed websites (AWK-7).
app.UseFrameAncestors();

if (appFiles is not null)
{
    app.UseResponseCompression();
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = appFiles });
    app.UseStaticFiles(appFileOptions);
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapOpenApi("/api/{documentName}/openapi.json");
app.MapControllers();

if (appFiles is not null)
{
    // Survey links (/s/{survey}) get the runner, built into s/ (AWK-6); other client-side routes get the designer.
    // /api and /health never get an app, so their 404s stay problem details.
    app.MapFallbackToFile("s/{*path:nonfile}", "s/index.html", appFileOptions);
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|health/|s/))}", "index.html", appFileOptions);
}

app.Run();

/// <summary>Entry point; public so the API tests can host it.</summary>
public partial class Program;
