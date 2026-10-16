// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Instances;
using Askwerk.Domain.Instances;

namespace Askwerk.Tests.Application.Fakes;

internal sealed class InMemoryInstanceRepository : IInstanceRepository
{
    public List<Instance> Pending { get; } = [];

    public List<Instance> Saved { get; } = [];

    public Task<Instance?> FindAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Saved.FirstOrDefault());

    public void Add(Instance instance) => Pending.Add(instance);
}
