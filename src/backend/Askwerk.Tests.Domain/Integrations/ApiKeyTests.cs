// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Domain.Integrations;

namespace Askwerk.Tests.Domain.Integrations;

public sealed class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("0193f2c1-0000-7000-8000-000000000001");
    private static readonly Guid Owner = Guid.Parse("0193f2c1-0000-7000-8000-000000000002");

    private static (ApiKey Key, string Secret) Issue(string name = "Reporting server", string[]? scopes = null, DateTimeOffset? expiresAt = null) =>
        ApiKey.Issue(Guid.CreateVersion7(Now), Tenant, name, scopes ?? ApiScopes.ReadResults, expiresAt, Owner, Now);

    [Fact]
    public void Issue_GivesTheSecretOnce_AndKeepsOnlyItsHash()
    {
        var (key, secret) = Issue();

        Assert.StartsWith("awk_", secret, StringComparison.Ordinal);
        Assert.Equal(48, secret.Length);
        Assert.Equal(secret[..12], key.Prefix);
        Assert.DoesNotContain(secret, key.SecretHash, StringComparison.Ordinal);
        Assert.Equal(secret[..8] + "…", key.Shown);
    }

    [Fact]
    public void Matches_TheSecret_ButNotAnotherOne()
    {
        var (key, secret) = Issue();
        var (_, other) = Issue();

        Assert.True(key.Matches(secret));
        Assert.False(key.Matches(other));
        Assert.False(key.Matches(secret[..47] + (secret[47] == 'a' ? 'b' : 'a')));
    }

    [Fact]
    public void Issue_WithoutAName_ThrowsAtTheName()
    {
        var error = Assert.Throws<DomainValidationException>(() => Issue(name: "  "));

        Assert.Equal(("name", "name-required"), (error.Field, error.Code));
    }

    [Fact]
    public void Issue_WithAScopeKeysMayNotHave_ThrowsAtTheScopes()
    {
        var error = Assert.Throws<DomainValidationException>(() => Issue(scopes: ["responses:read", "settings:write"]));

        Assert.Equal(("scopes", "unknown-scope"), (error.Field, error.Code));
    }

    [Fact]
    public void Presets_ReadResultsReads_FullAccessHasEveryScopeAKeyMayHave()
    {
        Assert.Equal(["surveys:read", "responses:read"], ApiScopes.ReadResults);
        Assert.Equal(["surveys:read", "surveys:write", "responses:read", "responses:write", "participants:read", "participants:write", "contacts:read", "contacts:write", "webhooks:manage"], ApiScopes.FullAccess);
    }

    [Fact]
    public void IsUsable_UntilItExpiresOrIsRevoked()
    {
        var (expiring, _) = Issue(expiresAt: Now.AddDays(90));
        var (revoked, _) = Issue();

        revoked.Revoke(Now.AddHours(1));

        Assert.True(expiring.IsUsable(Now.AddDays(89)));
        Assert.False(expiring.IsUsable(Now.AddDays(90)));
        Assert.False(revoked.IsUsable(Now.AddHours(2)));
        Assert.Equal(Now.AddHours(1), revoked.RevokedAt);
    }

    [Fact]
    public void Used_IsWrittenAtMostOnceAMinute()
    {
        var (key, _) = Issue();

        var first = key.Used(Now);
        var soon = key.Used(Now.AddSeconds(30));
        var later = key.Used(Now.AddSeconds(61));

        Assert.Equal((true, false, true), (first, soon, later));
        Assert.Equal(Now.AddSeconds(61), key.LastUsedAt);
    }
}
