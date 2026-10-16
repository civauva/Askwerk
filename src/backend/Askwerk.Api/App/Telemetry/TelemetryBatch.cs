// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel.DataAnnotations;

namespace Askwerk.Api.App.Telemetry;

/// <summary>A batch of technical signals from one browser app (agents/observability.md).</summary>
public sealed class TelemetryBatch : IValidatableObject
{
    /// <summary>Maximum records per batch.</summary>
    public const int MaxRecords = 50;

    private static readonly string[] Services = ["designer", "runner"];

    /// <summary>Which app sent it: <c>designer</c> or <c>runner</c>.</summary>
    [Required]
    public string Service { get; init; } = "";

    /// <summary>The signals, at most <see cref="MaxRecords"/>.</summary>
    [Required]
    public IReadOnlyList<TelemetryRecord> Records { get; init; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Services.Contains(Service))
        {
            yield return new ValidationResult("Service must be designer or runner.", [nameof(Service)]);
        }

        if (Records.Count > MaxRecords)
        {
            yield return new ValidationResult($"At most {MaxRecords} records per batch.", [nameof(Records)]);
        }
    }
}

/// <summary>One log record or metric value from the browser.</summary>
public sealed class TelemetryRecord
{
    /// <summary><c>log</c> or <c>metric</c>.</summary>
    [Required]
    [RegularExpression("^(log|metric)$")]
    public string Kind { get; init; } = "";

    /// <summary>For logs: <c>error</c>, <c>warn</c> or <c>info</c>.</summary>
    [RegularExpression("^(error|warn|info)$")]
    public string? Level { get; init; }

    /// <summary>Stable dotted name, e.g. <c>web.lcp</c> or <c>api.failed</c>.</summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9._-]{0,63}$")]
    public string Name { get; init; } = "";

    /// <summary>For logs: a short technical message; personal data is removed before logging.</summary>
    [MaxLength(500)]
    public string? Message { get; init; }

    /// <summary>For metrics: the measured value.</summary>
    public double? Value { get; init; }

    /// <summary>Route template or path without query string.</summary>
    [MaxLength(200)]
    public string? Route { get; init; }

    /// <summary>W3C trace id the browser used for the related API call, if any.</summary>
    [RegularExpression("^[0-9a-f]{32}$")]
    public string? TraceId { get; init; }
}
