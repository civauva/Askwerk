// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Tests.Application.Fakes;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
