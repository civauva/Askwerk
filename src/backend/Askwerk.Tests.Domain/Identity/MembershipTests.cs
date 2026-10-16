// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;

namespace Askwerk.Tests.Domain.Identity;

public sealed class MembershipTests
{
    [Theory]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Admin, true)]
    [InlineData(TenantRole.Editor, false)]
    [InlineData(TenantRole.Analyst, false)]
    [InlineData(TenantRole.Viewer, false)]
    public void RequiresTwoFactor_Role_OnlyForOwnersAndAdmins(TenantRole role, bool expected)
    {
        var required = role.RequiresTwoFactor();

        Assert.Equal(expected, required);
    }

    [Fact]
    public void UserCreate_Email_IsStoredTrimmedAndNormalisedForLookup()
    {
        var now = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

        var user = User.Create(Guid.CreateVersion7(now), " Anna@Acme.at ", "Anna Berger", now);

        Assert.Equal("Anna@Acme.at", user.Email);
        Assert.Equal("ANNA@ACME.AT", user.NormalizedEmail);
        Assert.Equal(UserSource.Local, user.Source);
    }

    [Fact]
    public void UserCreate_EmailWithoutAt_ThrowsWithTheField()
    {
        var now = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

        var error = Assert.Throws<DomainValidationException>(() => User.Create(Guid.CreateVersion7(now), "anna", "Anna", now));

        Assert.Equal("email", error.Field);
    }
}
