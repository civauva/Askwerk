// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using Askwerk.Application.Domains;

namespace Askwerk.Tests.Api;

/// <summary>Stands in for DNS: the records a test sets, nothing else.</summary>
internal sealed class FakeDns : IDnsLookup
{
    public ConcurrentDictionary<string, string> Cnames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, string[]> Txts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> CnameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Cnames.TryGetValue(name, out var target) ? target : null);

    public Task<IReadOnlyList<string>> TxtAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Txts.TryGetValue(name, out var texts) ? texts : []);
}
