// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using Askwerk.Tests.Integration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace Askwerk.Tests.Api;

/// <summary>Runs the real API in memory against a fresh PostgreSQL database (or an unreachable one).</summary>
internal sealed class AskwerkApiFactory(string ownerConnectionString, string dataDirectory) : WebApplicationFactory<Program>
{
    public const string DataKey = "dGVzdC1kYXRhLWtleS1ub3QtYS1zZWNyZXQtMzItYnl0ZXM=";

    public List<Activity> Spans { get; } = [];

    /// <summary>ASKWERK_DATA_DIR of the host, to check stored files.</summary>
    public string DataDirectory { get; } = dataDirectory;

    /// <summary>Owner connection to the test database, to check what is stored at rest.</summary>
    public string OwnerConnectionString { get; } = ownerConnectionString;

    /// <summary>A built-app stand-in: index.html and one asset.</summary>
    public string WebRoot { get; } = CreateWebRoot();

    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>Let the job service run jobs by itself, as in production; tests run them by hand otherwise.</summary>
    public bool RunJobsByThemselves { get; set; }

    /// <summary>Calls per minute per key on the Public API (AWK-25); the host's default when empty.</summary>
    public int? ApiRateLimit { get; set; }

    /// <summary>Seed the demo survey (AWK-23); off by default, so tests start with empty organisations.</summary>
    public bool DemoSurvey { get; set; }

    /// <summary>Gets more configuration values, e.g. installation settings fixed by the server (AWK-39).</summary>
    public Dictionary<string, string> Configuration { get; } = [];

    /// <summary>Every email Askwerk sent during the test.</summary>
    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Gets the integrator's server behind every webhook URL (AWK-32).</summary>
    public FakeWebhookReceiver Webhooks { get; } = new();

    /// <summary>Gets the OpenID Connect provider every organisation's single sign-on talks to (AWK-12).</summary>
    public FakeOidcProvider Oidc { get; } = new();

    /// <summary>The DNS records domains are checked against (AWK-7).</summary>
    internal FakeDns Dns { get; } = new();

    public static async Task<AskwerkApiFactory> WithFreshDatabaseAsync(PostgresServer server)
    {
        var owner = await server.CreateDatabaseAsync();

        return new AskwerkApiFactory(owner, NewDataDirectory());
    }

    public static AskwerkApiFactory WithUnreachableDatabase() =>
        new("Host=127.0.0.1;Port=1;Database=askwerk;Username=askwerk;Password=x;Timeout=1", NewDataDirectory());

    /// <summary>
    /// Waits until first start (migrations, instance, demo surveys) has finished, as the healthcheck would. Up to 45 s:
    /// the full run starts many apps at once, and first start then takes well over the 10 s it needs alone.
    /// </summary>
    public async Task WaitUntilReadyAsync(HttpClient client)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Askwerk did not become ready.");
    }

    /// <summary>A client that keeps cookies (like a browser), returned once first start has finished.</summary>
    public async Task<HttpClient> CreateReadyClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        await WaitUntilReadyAsync(client);

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ASKWERK_DB", OwnerConnectionString);
        builder.UseSetting("ASKWERK_DATA_KEY", DataKey);
        builder.UseSetting("ASKWERK_DATA_DIR", DataDirectory);
        builder.UseSetting("ASKWERK_VERSION", "0.0.0-test");
        builder.UseSetting("ASKWERK_WEB_ROOT", WebRoot);
        builder.UseSetting("ASKWERK_DEMO_SURVEY", DemoSurvey ? "on" : "off");

        // Background jobs run when a test asks for them (AWK-7), so what they do is checked, not raced.
        builder.UseSetting("ASKWERK_JOBS", RunJobsByThemselves ? "on" : "manual");

        foreach (var (key, value) in Configuration)
        {
            builder.UseSetting(key, value);
        }

        if (ApiRateLimit is { } limit)
        {
            builder.UseSetting("ASKWERK_API_RATE_LIMIT", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(logging => logging.AddProvider(Logs));
            services.AddSingleton<Askwerk.Application.Email.IEmailSender>(Emails);
            services.AddSingleton<Askwerk.Application.Domains.IDnsLookup>(Dns);
            services.AddHttpClient(Askwerk.Api.Integrations.DeliverWebhookJob.Client).ConfigurePrimaryHttpMessageHandler(Webhooks.Handler);
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(Spans));
            services.ConfigureAll<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(options => options.BackchannelHttpHandler = Oidc.Handler);
        });
    }

    private static string CreateWebRoot()
    {
        var path = NewDataDirectory();
        Directory.CreateDirectory(Path.Combine(path, "assets"));
        File.WriteAllText(Path.Combine(path, "index.html"), "<!doctype html><title>Askwerk</title><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(path, "assets", "app.js"), "console.log('askwerk');");
        Directory.CreateDirectory(Path.Combine(path, "s", "assets"));
        File.WriteAllText(Path.Combine(path, "s", "index.html"), "<!doctype html><title>Survey</title><div id=\"root\" data-app=\"runner\"></div>");
        File.WriteAllText(Path.Combine(path, "s", "assets", "runner.js"), "console.log('runner');");

        return path;
    }

    private static string NewDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "askwerk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        return path;
    }
}
