// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using Askwerk.Application.Telemetry;

namespace Askwerk.Api.Telemetry;

/// <summary>Reads the trace id of the current <see cref="Activity"/> (W3C trace context).</summary>
public sealed class ActivityTraceContext : ITraceContext
{
    /// <inheritdoc />
    public string? CurrentTraceId => Activity.Current?.TraceId.ToHexString();
}
