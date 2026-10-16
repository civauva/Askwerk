// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Askwerk.Api.App.Telemetry;

/// <summary>
/// Receives browser telemetry and feeds it into the backend's logging and metrics pipeline, so the browser
/// never talks to a telemetry backend directly (agents/observability.md).
/// </summary>
[ApiController]
[Route("api/app/telemetry")]
[ApiExplorerSettings(GroupName = "app")]
[EnableRateLimiting(TelemetryController.RateLimitPolicy)]
public sealed partial class TelemetryController(ILoggerFactory loggerFactory, FrontendMetrics metrics) : ControllerBase
{
    /// <summary>Rate limit policy name: 60 batches per minute per client address.</summary>
    public const string RateLimitPolicy = "telemetry";

    /// <summary>Accepts a batch of logs and metrics from the designer or the runner.</summary>
    /// <param name="batch">The batch.</param>
    /// <returns>202 when accepted; 400 when invalid; 429 when the client sends too much.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Post(TelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var service = "askwerk-" + batch.Service;
        var logger = loggerFactory.CreateLogger("Askwerk.Frontend");

        foreach (var record in batch.Records)
        {
            if (record.Kind == "metric" && record.Value is { } value)
            {
                metrics.Record(service, record.Name, value, StripQuery(record.Route));
                continue;
            }

            var level = record.Level switch
            {
                "error" => LogLevel.Error,
                "warn" => LogLevel.Warning,
                _ => LogLevel.Information,
            };

            var route = StripQuery(record.Route);
            var message = Scrub(record.Message);
            LogFrontendRecord(logger, level, service, record.Name, route, record.TraceId, message);
        }

        return Accepted();
    }

    // Free text from the browser may contain personal data; keep the shape of the message, not the data.
    private static string? Scrub(string? message)
    {
        if (message is null)
        {
            return null;
        }

        var withoutEmails = EmailPattern().Replace(message, "[email]");

        return LongNumberPattern().Replace(withoutEmails, "[number]");
    }

    private static string? StripQuery(string? route)
    {
        var index = route?.IndexOfAny(['?', '#']) ?? -1;

        return index < 0 ? route : route![..index];
    }

    [LoggerMessage(EventId = 2001, Message = "Frontend {Service} {Name} on {Route} (trace {ClientTraceId}): {Message}")]
    private static partial void LogFrontendRecord(ILogger logger, LogLevel level, string service, string name, string? route, string? clientTraceId, string? message);

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\d{6,}")]
    private static partial Regex LongNumberPattern();
}

/// <summary>Metrics forwarded from the browser apps (Web Vitals, API timings as the client saw them).</summary>
public sealed class FrontendMetrics : IDisposable
{
    /// <summary>Meter name; exported like every other <c>Askwerk.*</c> meter.</summary>
    public const string MeterName = "Askwerk.Frontend";

    private readonly Meter _meter = new(MeterName);
    private readonly Histogram<double> _values;

    /// <summary>Creates the meter and its histogram.</summary>
    public FrontendMetrics()
    {
        _values = _meter.CreateHistogram<double>("askwerk.frontend.value", description: "Values measured in the browser apps.");
    }

    /// <summary>Records one value.</summary>
    /// <param name="service">askwerk-designer or askwerk-runner.</param>
    /// <param name="name">The metric name, e.g. web.lcp.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="route">Route without query string.</param>
    public void Record(string service, string name, double value, string? route) =>
        _values.Record(value, new("service", service), new("name", name), new("route", route));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
