// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Instances;

/// <summary>This Askwerk installation. Exactly one exists once the first start has completed.</summary>
/// <param name="Id">Stable id of the installation.</param>
/// <param name="InitializedAt">When the first start completed (UTC).</param>
public sealed record Instance(Guid Id, DateTimeOffset InitializedAt);
