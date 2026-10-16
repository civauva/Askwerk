// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Askwerk.Api.App.Members;

/// <summary>
/// Organisation → Sign-in (AWK-12, W22): owners let a host product hand its users over and provision them, and map
/// the host's groups to roles. Off by default; Askwerk accounts stay on for owners.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Route("api/app/sign-in")]
public sealed class SignInSettingsController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace, ISecretProtector secrets, IOptionsMonitorCache<OpenIdConnectOptions> oidcOptions) : ControllerBase
{
    /// <summary>The organisation's sign-in settings; owners only.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The settings with the audience to give the host.</returns>
    [HttpGet]
    [ProducesResponseType<SignInSettings>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (await OwnerTenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        return Ok(View(await SettingsAsync(tenantId, cancellationToken), await SlugAsync(tenantId, cancellationToken)));
    }

    /// <summary>Saves the settings; owners only. Keys must be RSA or EC public keys in PEM.</summary>
    /// <param name="request">The settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved settings.</returns>
    [HttpPut]
    [ProducesResponseType<SignInSettings>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Put(SignInSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await OwnerTenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        var keys = (request.Keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).ToList();

        if (keys.Any(key => HandoverTokens.KeyFromPem(key) is null))
        {
            ModelState.AddModelError("keys", "key-invalid");
            return ValidationProblem(ModelState);
        }

        var rules = new List<RoleRule>();

        foreach (var rule in request.RoleRules ?? [])
        {
            if (!TryRole(rule.Role, out var role))
            {
                ModelState.AddModelError("roleRules", "role-invalid");
                return ValidationProblem(ModelState);
            }

            rules.Add(new RoleRule(rule.Group ?? "", role));
        }

        TenantRole? defaultRole = null;

        if (request.DefaultRole is not null)
        {
            if (!TryRole(request.DefaultRole, out var role))
            {
                ModelState.AddModelError("defaultRole", "role-invalid");
                return ValidationProblem(ModelState);
            }

            defaultRole = role;
        }

        var settings = await context.TenantSignIns.FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken);

        if (settings is null)
        {
            settings = TenantSignIn.Default(tenantId);
            context.TenantSignIns.Add(settings);
        }

        try
        {
            settings.Change(request.HandoverEnabled, request.Issuer, keys, request.JwksUrl, request.ProvisioningEnabled, request.SystemName, rules, defaultRole, time.GetUtcNow());
            if (request.LdapCaCertificate is { Length: > 0 } pem && !ValidCertificate(pem))
            {
                ModelState.AddModelError("ldapCaCertificate", "certificate-invalid");
                return ValidationProblem(ModelState);
            }

            settings.ChangeLdap(
                request.LdapEnabled,
                request.LdapHost,
                request.LdapPort ?? 636,
                request.LdapBindDn,
                string.IsNullOrEmpty(request.LdapBindPassword) ? null : secrets.Protect(request.LdapBindPassword),
                keepPassword: request.LdapBindPassword is null,
                request.LdapBaseDn,
                request.LdapLoginAttribute,
                request.LdapObjectClass,
                request.LdapGroupAttribute,
                request.LdapCaCertificate,
                request.LdapDisplayName,
                time.GetUtcNow());
            settings.ChangeOidc(
                request.OidcEnabled,
                request.OidcAuthority,
                request.OidcClientId,
                string.IsNullOrEmpty(request.OidcClientSecret) ? null : secrets.Protect(request.OidcClientSecret),
                keepSecret: request.OidcClientSecret is null,
                request.OidcDisplayName,
                time.GetUtcNow());
        }
        catch (DomainValidationException error)
        {
            ModelState.AddModelError(error.Field, error.Code);
            return ValidationProblem(ModelState);
        }

        var diff = new { handover = settings.HandoverEnabled, issuer = settings.HandoverIssuer, keys = settings.HandoverKeys.Length, jwksUrl = settings.HandoverJwksUrl, provisioning = settings.ProvisioningEnabled, systemName = settings.SystemName, rules = rules.Count, defaultRole = request.DefaultRole, oidc = settings.OidcEnabled, oidcAuthority = settings.OidcAuthority, oidcClientId = settings.OidcClientId, ldap = settings.LdapEnabled, ldapHost = settings.LdapHost, ldapBaseDn = settings.LdapBaseDn };
        await audit.WriteAsync(new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, User.UserId().ToString()!, "sign-in.changed", "TenantSignIn", tenantId.ToString(), JsonSerializer.Serialize(diff), trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Oidc.Forget(oidcOptions, tenantId);

        return Ok(View(settings, await SlugAsync(tenantId, cancellationToken)));
    }

    /// <summary>Tries the directory with a test person's name and password; owners only. Nothing is stored.</summary>
    /// <param name="request">The test person.</param>
    /// <param name="directory">The directory check.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the directory says about them, and the role they would get.</returns>
    [HttpPost("ldap-test")]
    [ProducesResponseType<DirectoryTestResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TestDirectory(DirectoryTestRequest request, [FromServices] DirectoryPasswords directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await OwnerTenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        var settings = await SettingsAsync(tenantId, cancellationToken);

        if (settings.LdapHost is null || settings.LdapBindDn is null || settings.LdapBaseDn is null)
        {
            return Ok(new DirectoryTestResult(false, "ldap-incomplete", null, null, [], null));
        }

        var (person, problem) = await directory.CheckAsync(settings, request.Username ?? "", request.Password ?? "", cancellationToken);

        return Ok(person is null
            ? new DirectoryTestResult(false, problem, null, null, [], null)
            : new DirectoryTestResult(true, null, person.Email, person.Name, person.Groups, settings.RoleFor(person.Groups)?.ToString().ToLowerInvariant()));
    }

    private static bool ValidCertificate(string pem)
    {
        try
        {
            using var certificate = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(pem);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static bool TryRole(string? value, out TenantRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && !int.TryParse(value, out _) && role != TenantRole.Owner;

    private Task<string> SlugAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.Slug).FirstAsync(cancellationToken);

    private static SignInSettings View(TenantSignIn settings, string slug) =>
        new(
            settings.HandoverEnabled,
            settings.HandoverIssuer,
            settings.HandoverKeys,
            settings.HandoverJwksUrl,
            TenantSignIn.AudienceOf(settings.TenantId),
            settings.ProvisioningEnabled,
            settings.SystemName,
            [.. settings.RoleRules.Select(rule => new RoleRuleItem(rule.Group, rule.Role.ToString().ToLowerInvariant()))],
            settings.DefaultRole?.ToString().ToLowerInvariant(),
            settings.OidcEnabled,
            settings.OidcAuthority,
            settings.OidcClientId,
            settings.OidcClientSecretProtected is not null,
            settings.OidcDisplayName,
            Oidc.CallbackOf(settings.TenantId),
            $"/sso/{slug}",
            settings.LdapEnabled,
            settings.LdapHost,
            settings.LdapPort,
            settings.LdapBindDn,
            settings.LdapBindPasswordProtected is not null,
            settings.LdapBaseDn,
            settings.LdapLoginAttribute,
            settings.LdapObjectClass,
            settings.LdapGroupAttribute,
            settings.LdapCaCertificate,
            settings.LdapDisplayName);

    private async Task<TenantSignIn> SettingsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await context.TenantSignIns.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == tenantId, cancellationToken) ?? TenantSignIn.Default(tenantId);

    private async Task<Guid?> OwnerTenantAsync(CancellationToken cancellationToken)
    {
        if (User.TenantId() is not { } tenantId || User.UserId() is not { } userId)
        {
            return null;
        }

        var owner = await context.Memberships.AnyAsync(row => row.TenantId == tenantId && row.UserId == userId && row.Role == TenantRole.Owner, cancellationToken);

        return owner ? tenantId : null;
    }
}

/// <summary>A role rule as sent and shown.</summary>
/// <param name="Group">The host's group or role name.</param>
/// <param name="Role">admin, editor, analyst or viewer.</param>
public sealed record RoleRuleItem(string? Group, string? Role);

/// <summary>Sign-in settings to save.</summary>
/// <param name="HandoverEnabled">Whether the host may hand users over.</param>
/// <param name="Issuer">The iss of the host's tokens.</param>
/// <param name="Keys">The host's public keys in PEM, at most two.</param>
/// <param name="JwksUrl">Or an https URL of the host's JWKS.</param>
/// <param name="ProvisioningEnabled">Whether the provisioning API is on.</param>
/// <param name="SystemName">The host system's name for "Managed by …".</param>
/// <param name="RoleRules">Groups to roles, first match wins.</param>
/// <param name="DefaultRole">The role of anyone else; null lets them not in.</param>
/// <param name="OidcEnabled">Whether single sign-on is on.</param>
/// <param name="OidcAuthority">The provider's https issuer URL.</param>
/// <param name="OidcClientId">Askwerk's client id at the provider.</param>
/// <param name="OidcClientSecret">A new client secret; null keeps the stored one, empty removes it.</param>
/// <param name="OidcDisplayName">The name on the button: "Sign in with …".</param>
/// <param name="LdapEnabled">Whether people sign in with their directory password.</param>
/// <param name="LdapHost">The directory's host name (LDAPS).</param>
/// <param name="LdapPort">The LDAPS port; 636 when empty.</param>
/// <param name="LdapBindDn">The DN of the account that searches.</param>
/// <param name="LdapBindPassword">A new password for it; null keeps the stored one.</param>
/// <param name="LdapBaseDn">Where people are searched.</param>
/// <param name="LdapLoginAttribute">uid, sAMAccountName or mail.</param>
/// <param name="LdapObjectClass">person, user or inetOrgPerson.</param>
/// <param name="LdapGroupAttribute">The attribute with a person's groups, e.g. memberOf.</param>
/// <param name="LdapCaCertificate">The directory's CA certificate (PEM) when it is internal.</param>
/// <param name="LdapDisplayName">The directory's name on the sign-in form.</param>
public sealed record SignInSettingsRequest(
    bool HandoverEnabled,
    string? Issuer,
    IReadOnlyList<string>? Keys,
    string? JwksUrl,
    bool ProvisioningEnabled,
    string? SystemName,
    IReadOnlyList<RoleRuleItem>? RoleRules,
    string? DefaultRole,
    bool OidcEnabled = false,
    string? OidcAuthority = null,
    string? OidcClientId = null,
    string? OidcClientSecret = null,
    string? OidcDisplayName = null,
    bool LdapEnabled = false,
    string? LdapHost = null,
    int? LdapPort = null,
    string? LdapBindDn = null,
    string? LdapBindPassword = null,
    string? LdapBaseDn = null,
    string? LdapLoginAttribute = null,
    string? LdapObjectClass = null,
    string? LdapGroupAttribute = null,
    string? LdapCaCertificate = null,
    string? LdapDisplayName = null);

/// <summary>The sign-in settings.</summary>
/// <param name="HandoverEnabled">Whether the host may hand users over.</param>
/// <param name="Issuer">The iss of the host's tokens.</param>
/// <param name="Keys">The host's public keys in PEM.</param>
/// <param name="JwksUrl">The host's JWKS URL.</param>
/// <param name="Audience">The aud the host puts in its tokens.</param>
/// <param name="ProvisioningEnabled">Whether the provisioning API is on.</param>
/// <param name="SystemName">The host system's name.</param>
/// <param name="RoleRules">Groups to roles.</param>
/// <param name="DefaultRole">The role of anyone else; null: not let in.</param>
/// <param name="OidcEnabled">Whether single sign-on is on.</param>
/// <param name="OidcAuthority">The provider's issuer URL.</param>
/// <param name="OidcClientId">Askwerk's client id at the provider.</param>
/// <param name="OidcClientSecretSet">Whether a client secret is stored; it is never shown.</param>
/// <param name="OidcDisplayName">The name on the button.</param>
/// <param name="OidcCallbackPath">The redirect URI's path to register at the provider.</param>
/// <param name="SsoPath">The link that starts single sign-on for this organisation.</param>
/// <param name="LdapEnabled">Whether people sign in with their directory password.</param>
/// <param name="LdapHost">The directory's host name.</param>
/// <param name="LdapPort">The LDAPS port.</param>
/// <param name="LdapBindDn">The DN of the account that searches.</param>
/// <param name="LdapBindPasswordSet">Whether its password is stored; it is never shown.</param>
/// <param name="LdapBaseDn">Where people are searched.</param>
/// <param name="LdapLoginAttribute">The attribute people sign in with.</param>
/// <param name="LdapObjectClass">The object class of people.</param>
/// <param name="LdapGroupAttribute">The attribute with a person's groups.</param>
/// <param name="LdapCaCertificate">The directory's CA certificate, if internal.</param>
/// <param name="LdapDisplayName">The directory's name on the sign-in form.</param>
public sealed record SignInSettings(
    bool HandoverEnabled,
    string? Issuer,
    IReadOnlyList<string> Keys,
    string? JwksUrl,
    string Audience,
    bool ProvisioningEnabled,
    string? SystemName,
    IReadOnlyList<RoleRuleItem> RoleRules,
    string? DefaultRole,
    bool OidcEnabled,
    string? OidcAuthority,
    string? OidcClientId,
    bool OidcClientSecretSet,
    string? OidcDisplayName,
    string OidcCallbackPath,
    string SsoPath,
    bool LdapEnabled,
    string? LdapHost,
    int LdapPort,
    string? LdapBindDn,
    bool LdapBindPasswordSet,
    string? LdapBaseDn,
    string LdapLoginAttribute,
    string LdapObjectClass,
    string LdapGroupAttribute,
    string? LdapCaCertificate,
    string? LdapDisplayName);

/// <summary>A test person for the directory.</summary>
/// <param name="Username">Their user name.</param>
/// <param name="Password">Their password; never stored.</param>
public sealed record DirectoryTestRequest(string? Username, string? Password);

/// <summary>What the directory says about the test person.</summary>
/// <param name="Ok">Whether the sign-in would work.</param>
/// <param name="Problem">ldap-incomplete, directory-unreachable, service-account-refused, invalid-credentials or no-email.</param>
/// <param name="Email">Their address.</param>
/// <param name="Name">Their name.</param>
/// <param name="Groups">Their groups.</param>
/// <param name="Role">The role they would get; null: not let in.</param>
public sealed record DirectoryTestResult(bool Ok, string? Problem, string? Email, string? Name, IReadOnlyList<string> Groups, string? Role);
