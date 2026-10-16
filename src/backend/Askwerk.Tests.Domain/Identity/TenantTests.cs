// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;

namespace Askwerk.Tests.Domain.Identity;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("acme")]
    [InlineData("acme-gmbh")]
    [InlineData("a1")]
    public void IsValidSlug_LowercaseLettersDigitsAndInnerHyphens_IsTrue(string slug)
    {
        var valid = Tenant.IsValidSlug(slug);

        Assert.True(valid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Acme")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme gmbh")]
    [InlineData("ac--me")]
    [InlineData("abcdefghijklmnopqrstuvwxyz-abcdefghijklmnopq")]
    public void IsValidSlug_AnythingElse_IsFalse(string slug)
    {
        var valid = Tenant.IsValidSlug(slug);

        Assert.False(valid);
    }

    [Theory]
    [InlineData("Acme GmbH", "acme-gmbh")]
    [InlineData("Bäckerei Müller & Söhne", "baeckerei-mueller-soehne")]
    [InlineData("  Verein   Lesefreunde ", "verein-lesefreunde")]
    [InlineData("Straße 1", "strasse-1")]
    public void SuggestSlug_Name_GivesAValidSlug(string name, string expected)
    {
        var slug = Tenant.SuggestSlug(name);

        Assert.Equal(expected, slug);
    }

    [Fact]
    public void Create_ValidNameAndSlug_TrimsTheName()
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(Now), "  Acme GmbH ", "acme", Now);

        Assert.Equal("Acme GmbH", tenant.Name);
        Assert.Equal("acme", tenant.Slug);
        Assert.Equal(Now, tenant.CreatedAt);
    }

    [Fact]
    public void Create_InvalidSlug_ThrowsWithTheField()
    {
        var error = Assert.Throws<DomainValidationException>(() => Tenant.Create(Guid.CreateVersion7(Now), "Acme", "Acme!", Now));

        Assert.Equal("slug", error.Field);
        Assert.Equal("slug-invalid", error.Code);
    }

    [Fact]
    public void Create_BlankName_ThrowsWithTheField()
    {
        var error = Assert.Throws<DomainValidationException>(() => Tenant.Create(Guid.CreateVersion7(Now), "  ", "acme", Now));

        Assert.Equal("name", error.Field);
        Assert.Equal("name-required", error.Code);
    }
}
