// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Telemetry;

namespace Askwerk.Tests.Integration;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class NoTraceContext : ITraceContext
{
    public string? CurrentTraceId => null;
}
