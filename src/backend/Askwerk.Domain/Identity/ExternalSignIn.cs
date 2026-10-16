// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Identity;

/// <summary>A rule of "Roles from your system": a group or role name of the host gives an Askwerk role.</summary>
/// <param name="Group">The host's group or role name, compared without case.</param>
/// <param name="Role">The Askwerk role; never Owner.</param>
public sealed record RoleRule(string Group, TenantRole Role);

/// <summary>
/// How an organisation lets people sign in besides Askwerk accounts (AWK-12, W22). Off by default; owners always
/// keep their Askwerk account as a way in when a provider fails.
/// </summary>
public sealed class TenantSignIn
{
    /// <summary>At most two keys, so a host can rotate them.</summary>
    public const int MaxKeys = 2;

    private TenantSignIn()
    {
    }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets a value indicating whether a host product may sign people in with a signed token.</summary>
    public bool HandoverEnabled { get; private set; }

    /// <summary>Gets the iss every hand-over token must carry.</summary>
    public string? HandoverIssuer { get; private set; }

    /// <summary>Gets the host's public keys (PEM), at most two.</summary>
    public string[] HandoverKeys { get; private set; } = [];

    /// <summary>Gets an https URL of the host's JWKS, instead of or besides the PEM keys.</summary>
    public string? HandoverJwksUrl { get; private set; }

    /// <summary>Gets a value indicating whether the host manages users through the provisioning API.</summary>
    public bool ProvisioningEnabled { get; private set; }

    /// <summary>Gets the host system's name, shown on the Members page ("Managed by HR Suite").</summary>
    public string? SystemName { get; private set; }

    /// <summary>Gets a value indicating whether people may sign in with the organisation's OpenID Connect provider (step 2).</summary>
    public bool OidcEnabled { get; private set; }

    /// <summary>Gets the provider's issuer URL; its discovery document is read from there.</summary>
    public string? OidcAuthority { get; private set; }

    /// <summary>Gets the client id Askwerk has at the provider.</summary>
    public string? OidcClientId { get; private set; }

    /// <summary>Gets the client secret, protected at rest; null for a public client (PKCE only).</summary>
    public string? OidcClientSecretProtected { get; private set; }

    /// <summary>Gets the button text's name: "Sign in with {name}".</summary>
    public string? OidcDisplayName { get; private set; }

    /// <summary>Gets a value indicating whether people may sign in with their directory password (LDAP, step 3).</summary>
    public bool LdapEnabled { get; private set; }

    /// <summary>Gets the directory server's host name; Askwerk connects with LDAPS only.</summary>
    public string? LdapHost { get; private set; }

    /// <summary>Gets the LDAPS port, 636 by default.</summary>
    public int LdapPort { get; private set; } = 636;

    /// <summary>Gets the DN of the account that searches the directory.</summary>
    public string? LdapBindDn { get; private set; }

    /// <summary>Gets that account's password, protected at rest.</summary>
    public string? LdapBindPasswordProtected { get; private set; }

    /// <summary>Gets where people are searched, e.g. ou=people,dc=acme,dc=at.</summary>
    public string? LdapBaseDn { get; private set; }

    /// <summary>Gets the attribute people sign in with: uid, sAMAccountName or mail.</summary>
    public string LdapLoginAttribute { get; private set; } = "uid";

    /// <summary>Gets the object class of people, e.g. person or user.</summary>
    public string LdapObjectClass { get; private set; } = "person";

    /// <summary>Gets the attribute holding a person's groups, e.g. memberOf.</summary>
    public string LdapGroupAttribute { get; private set; } = "memberOf";

    /// <summary>Gets the CA certificate (PEM) of a directory with an internal CA; the system's roots otherwise.</summary>
    public string? LdapCaCertificate { get; private set; }

    /// <summary>Gets the directory's name on the sign-in form: "Sign in with {name}".</summary>
    public string? LdapDisplayName { get; private set; }

    /// <summary>Gets the host groups that give roles, first match wins.</summary>
    public IReadOnlyList<RoleRule> RoleRules { get; private set; } = [];

    /// <summary>Gets the role of anyone no rule matches; null lets them not in.</summary>
    public TenantRole? DefaultRole { get; private set; } = TenantRole.Viewer;

    /// <summary>Gets when it last changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The audience a host puts in its tokens for this organisation.</summary>
    public static string AudienceOf(Guid tenantId) => $"askwerk:{tenantId}";

    /// <summary>The tenant a hand-over audience names, if it is one.</summary>
    public static Guid? TenantOfAudience(string? audience) =>
        audience is not null && audience.StartsWith("askwerk:", StringComparison.Ordinal) && Guid.TryParse(audience["askwerk:".Length..], out var tenant) ? tenant : null;

    /// <summary>The settings of an organisation that has not set anything: everything off.</summary>
    public static TenantSignIn Default(Guid tenantId) => new() { TenantId = tenantId };

    /// <summary>Changes the settings; the caller checks the keys (they must parse).</summary>
    public void Change(bool handover, string? issuer, IReadOnlyList<string> keys, string? jwksUrl, bool provisioning, string? systemName, IReadOnlyList<RoleRule> rules, TenantRole? defaultRole, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(rules);
        issuer = string.IsNullOrWhiteSpace(issuer) ? null : issuer.Trim();
        jwksUrl = string.IsNullOrWhiteSpace(jwksUrl) ? null : jwksUrl.Trim();

        if (handover && issuer is null)
        {
            throw new DomainValidationException("issuer", "issuer-required");
        }

        if (handover && keys.Count == 0 && jwksUrl is null)
        {
            throw new DomainValidationException("keys", "key-required");
        }

        if (keys.Count > MaxKeys)
        {
            throw new DomainValidationException("keys", "too-many-keys");
        }

        if (jwksUrl is not null && !(Uri.TryCreate(jwksUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps))
        {
            throw new DomainValidationException("jwksUrl", "https-required");
        }

        if (rules.Any(rule => rule.Role == TenantRole.Owner) || defaultRole == TenantRole.Owner)
        {
            throw new DomainValidationException("roleRules", "owner-not-mapped");
        }

        if (rules.Any(rule => string.IsNullOrWhiteSpace(rule.Group)))
        {
            throw new DomainValidationException("roleRules", "group-required");
        }

        HandoverEnabled = handover;
        HandoverIssuer = issuer;
        HandoverKeys = [.. keys.Select(key => key.Trim())];
        HandoverJwksUrl = jwksUrl;
        ProvisioningEnabled = provisioning;
        SystemName = string.IsNullOrWhiteSpace(systemName) ? null : systemName.Trim();
        RoleRules = [.. rules.Select(rule => rule with { Group = rule.Group.Trim() })];
        DefaultRole = defaultRole;
        UpdatedAt = now;
    }

    /// <summary>Changes single sign-on; a null secret keeps the stored one, an empty one removes it.</summary>
    public void ChangeOidc(bool enabled, string? authority, string? clientId, string? clientSecretProtected, bool keepSecret, string? displayName, DateTimeOffset now)
    {
        authority = string.IsNullOrWhiteSpace(authority) ? null : authority.Trim().TrimEnd('/');
        clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();

        if (authority is not null && !(Uri.TryCreate(authority, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps))
        {
            throw new DomainValidationException("oidcAuthority", "https-required");
        }

        if (enabled && authority is null)
        {
            throw new DomainValidationException("oidcAuthority", "authority-required");
        }

        if (enabled && clientId is null)
        {
            throw new DomainValidationException("oidcClientId", "client-id-required");
        }

        OidcEnabled = enabled;
        OidcAuthority = authority;
        OidcClientId = clientId;
        OidcClientSecretProtected = keepSecret ? OidcClientSecretProtected : clientSecretProtected;
        OidcDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAt = now;
    }

    /// <summary>Changes the directory sign-in; a null password keeps the stored one. The caller checks the CA certificate.</summary>
    public void ChangeLdap(bool enabled, string? host, int port, string? bindDn, string? bindPasswordProtected, bool keepPassword, string? baseDn, string? loginAttribute, string? objectClass, string? groupAttribute, string? caCertificate, string? displayName, DateTimeOffset now)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static bool Attribute(string value) => value.Length <= 64 && char.IsAsciiLetter(value[0]) && value.All(each => char.IsAsciiLetterOrDigit(each) || each == '-');

        host = Clean(host);
        bindDn = Clean(bindDn);
        baseDn = Clean(baseDn);
        var login = Clean(loginAttribute) ?? "uid";
        var kind = Clean(objectClass) ?? "person";
        var groups = Clean(groupAttribute) ?? "memberOf";

        if (enabled && (host is null || bindDn is null || baseDn is null))
        {
            throw new DomainValidationException(host is null ? "ldapHost" : bindDn is null ? "ldapBindDn" : "ldapBaseDn", "ldap-incomplete");
        }

        if (host is not null && (host.Length > 253 || host.Contains(' ', StringComparison.Ordinal) || host.Contains('/', StringComparison.Ordinal)))
        {
            throw new DomainValidationException("ldapHost", "host-invalid");
        }

        if (port is < 1 or > 65535)
        {
            throw new DomainValidationException("ldapPort", "port-invalid");
        }

        if (!Attribute(login) || !Attribute(kind) || !Attribute(groups))
        {
            throw new DomainValidationException("ldapLoginAttribute", "attribute-invalid");
        }

        LdapEnabled = enabled;
        LdapHost = host;
        LdapPort = port;
        LdapBindDn = bindDn;
        LdapBindPasswordProtected = keepPassword ? LdapBindPasswordProtected : bindPasswordProtected;
        LdapBaseDn = baseDn;
        LdapLoginAttribute = login;
        LdapObjectClass = kind;
        LdapGroupAttribute = groups;
        LdapCaCertificate = Clean(caCertificate);
        LdapDisplayName = Clean(displayName);
        UpdatedAt = now;
    }

    /// <summary>The role for a host user's groups: the first rule that matches, else the default (null: not let in).</summary>
    public TenantRole? RoleFor(IEnumerable<string> groups)
    {
        var set = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RoleRules.FirstOrDefault(rule => set.Contains(rule.Group))?.Role ?? DefaultRole;
    }
}

/// <summary>A host user known in an organisation: the host's id, bound to an Askwerk user (W22).</summary>
public sealed class ExternalIdentity
{
    private ExternalIdentity()
    {
        ExternalId = "";
        Source = "";
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the Askwerk user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the host's user id (sub of a hand-over token, or the provisioning API's externalId).</summary>
    public string ExternalId { get; private set; }

    /// <summary>Gets how it came: handover or provisioning.</summary>
    public string Source { get; private set; }

    /// <summary>Gets a value indicating whether the host user may sign in; false after deactivation.</summary>
    public bool Active { get; private set; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when it last changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Binds a host user id to an Askwerk user.</summary>
    public static ExternalIdentity Create(Guid id, Guid tenantId, Guid userId, string externalId, string source, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > 200)
        {
            throw new DomainValidationException("externalId", "external-id-invalid");
        }

        return new ExternalIdentity { Id = id, TenantId = tenantId, UserId = userId, ExternalId = externalId, Source = source, Active = true, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>Activates or deactivates the host user; provisioning takes over a hand-over identity.</summary>
    public void SetActive(bool active, string source, DateTimeOffset now)
    {
        Active = active;
        Source = source == "provisioning" ? source : Source;
        UpdatedAt = now;
    }
}

/// <summary>A hand-over token already used (its jti), kept until it expires so it cannot sign in twice.</summary>
public sealed record HandoverTokenUse(Guid TenantId, string Jti, DateTimeOffset ExpiresAt);
