// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Application.Telemetry;

/// <summary>The W3C trace id of the request or job being handled, for audit entries and error references.</summary>
public interface ITraceContext
{
    /// <summary>The current trace id as 32 lowercase hex characters, or null outside a trace.</summary>
    string? CurrentTraceId { get; }
}
