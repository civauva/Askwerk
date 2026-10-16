// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Themes;

/// <summary>
/// A look of the organisation's surveys (AWK-17, W10 A): a name and the tokens. One theme per organisation is the
/// default; a survey uses it unless it picks another.
/// </summary>
public sealed class Theme
{
    private Theme()
    {
        Name = "";
        Tokens = "{}";
    }

    private Theme(Guid id, Guid tenantId, string name, string tokens, bool isDefault, Guid createdBy, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Tokens = tokens;
        IsDefault = isDefault;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Gets the theme's id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the name shown in the lists.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the tokens as JSON (<see cref="ThemeTokens"/>), checked by <see cref="ThemeChecks"/> before saving.</summary>
    public string Tokens { get; private set; }

    /// <summary>Gets a value indicating whether surveys without their own theme use this one.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Gets who created it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Gets when it was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new theme; the caller has checked the tokens.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="name">Its name.</param>
    /// <param name="tokens">The tokens as JSON.</param>
    /// <param name="isDefault">Whether it becomes the default (the organisation's first theme does).</param>
    /// <param name="createdBy">The user.</param>
    /// <param name="now">The time.</param>
    /// <returns>The theme.</returns>
    public static Theme Create(Guid id, Guid tenantId, string name, string tokens, bool isDefault, Guid createdBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokens);

        return new Theme(id, tenantId, name.Trim(), tokens, isDefault, createdBy, now);
    }

    /// <summary>Changes name and tokens; the caller has checked the tokens.</summary>
    /// <param name="name">New name.</param>
    /// <param name="tokens">New tokens as JSON.</param>
    /// <param name="now">The time.</param>
    public void Change(string name, string tokens, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokens);
        Name = name.Trim();
        Tokens = tokens;
        UpdatedAt = now;
    }

    /// <summary>Makes it the default, or not.</summary>
    /// <param name="isDefault">Whether it is the default.</param>
    /// <param name="now">The time.</param>
    public void SetDefault(bool isDefault, DateTimeOffset now)
    {
        IsDefault = isDefault;
        UpdatedAt = now;
    }
}
