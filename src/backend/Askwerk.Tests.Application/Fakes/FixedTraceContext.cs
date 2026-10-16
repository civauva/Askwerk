// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Telemetry;

namespace Askwerk.Tests.Application.Fakes;

internal sealed class FixedTraceContext(string? traceId) : ITraceContext
{
    public string? CurrentTraceId => traceId;
}
