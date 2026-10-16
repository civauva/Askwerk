// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Askwerk.Api.App.Session;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Askwerk.Api.Identity;

/// <summary>A person the directory knows: a stable id, address, name and groups.</summary>
/// <param name="Id">entryUUID, objectGUID or, failing both, the DN.</param>
/// <param name="Email">mail.</param>
/// <param name="Name">displayName or cn.</param>
/// <param name="Groups">The group attribute's values, and the CN of each that is a DN.</param>
public sealed record DirectoryUser(string Id, string Email, string Name, IReadOnlyList<string> Groups);

/// <summary>
/// Checks a password against the organisation's directory (AWK-12, W22 step 3): the service account searches the
/// person by the login attribute, then the person's own DN binds with the password. Nothing is stored.
/// </summary>
public sealed class DirectoryPasswords(ISecretProtector secrets)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The person, or a problem: directory-unreachable, service-account-refused, invalid-credentials, no-email.</summary>
    public async Task<(DirectoryUser? User, string? Problem)> CheckAsync(TenantSignIn settings, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || username.Length > 256)
        {
            return (null, "invalid-credentials");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var ca = settings.LdapCaCertificate is { } pem ? X509Certificate2.CreateFromPem(pem) : null;

        try
        {
            await using var connection = await LdapConnection.ConnectAsync(settings.LdapHost!, settings.LdapPort, ca, timeout.Token);
            var servicePassword = settings.LdapBindPasswordProtected is { } protectedPassword ? secrets.Unprotect(protectedPassword) : "";

            if (await connection.BindAsync(settings.LdapBindDn!, servicePassword, timeout.Token) != LdapConnection.Success)
            {
                return (null, "service-account-refused");
            }

            string[] attributes = ["mail", "displayName", "cn", "entryUUID", "objectGUID", settings.LdapGroupAttribute];
            var found = await connection.SearchAsync(settings.LdapBaseDn!, [("objectClass", settings.LdapObjectClass), (settings.LdapLoginAttribute, username.Trim())], attributes, 2, timeout.Token);

            if (found.Count != 1)
            {
                return (null, "invalid-credentials");
            }

            var entry = found[0];

            if (await connection.BindAsync(entry.Dn, password, timeout.Token) != LdapConnection.Success)
            {
                return (null, "invalid-credentials");
            }

            if (entry.Text("mail") is not { Length: > 0 } email)
            {
                return (null, "no-email");
            }

            var id = entry.Text("entryUUID")
                ?? (entry.Attributes.TryGetValue("objectGUID", out var guid) && guid.Count > 0 && guid[0].Length == 16 ? new Guid(guid[0]).ToString() : null)
                ?? entry.Dn;
            var groups = entry.Texts(settings.LdapGroupAttribute).SelectMany(group => CommonName(group) is { } cn ? new[] { group, cn } : [group]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            return (new DirectoryUser(id, email, entry.Text("displayName") ?? entry.Text("cn") ?? username, groups), null);
        }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or LdapException or OperationCanceledException or CryptographicException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return (null, "directory-unreachable");
        }
    }

    /// <summary>The value of the first RDN when it is a CN: "cn=HR-Admins,ou=groups,…" → HR-Admins.</summary>
    private static string? CommonName(string value)
    {
        if (!value.StartsWith("cn=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var end = value.IndexOf(',', StringComparison.Ordinal);

        return end < 0 ? value[3..] : value[3..end];
    }
}

/// <summary>
/// How an organisation's people sign in when it is not with an Askwerk password (AWK-12): single sign-on or the
/// directory, chosen by the organisation's short name on the sign-in page.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Route("api/app/session")]
public sealed class DirectorySignInController(
    AskwerkDbContext context,
    DbSessionContext session,
    DirectoryPasswords directory,
    ExternalUsers externalUsers,
    SessionService sessions,
    IUnitOfWork unitOfWork,
    IMemoryCache attempts) : ControllerBase
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    /// <summary>Which ways an organisation's people sign in: single sign-on, the directory, or both.</summary>
    /// <param name="organisation">The organisation's short name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The ways with their names; 404 when the organisation has neither.</returns>
    [HttpGet("sso/{organisation}")]
    [AllowAnonymous]
    [ProducesResponseType<SingleSignOnOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Options(string organisation, CancellationToken cancellationToken) =>
        await OptionsAsync(organisation, cancellationToken) is { } found ? Ok(new SingleSignOnOptions(found.Oidc, found.Ldap, found.Oidc ? found.OidcName : null, found.Ldap ? found.LdapName : null)) : NotFound();

    /// <summary>Signs in with the directory's user name and password; the person gets the role of the first matching rule.</summary>
    /// <param name="request">Organisation, user name and password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 401 invalid-credentials; 403 no-access or locked; 404; 502 directory-unreachable.</returns>
    [HttpPost("ldap")]
    [AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SignInProblem>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<SignInProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<SignInProblem>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SignIn(DirectorySignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await OptionsAsync(request.Organisation ?? "", cancellationToken) is not { Ldap: true } found)
        {
            return NotFound();
        }

        var key = $"ldap-failures:{found.Tenant}:{(request.Username ?? "").Trim().ToUpperInvariant()}";

        if (attempts.TryGetValue(key, out int failures) && failures >= MaxFailures)
        {
            return Refused(StatusCodes.Status403Forbidden, "locked");
        }

        session.TenantId = found.Tenant;
        await context.Database.CloseConnectionAsync();
        var settings = await context.TenantSignIns.AsNoTracking().SingleAsync(row => row.TenantId == found.Tenant, cancellationToken);
        var (person, problem) = await directory.CheckAsync(settings, request.Username ?? "", request.Password ?? "", cancellationToken);

        if (person is null)
        {
            if (problem == "invalid-credentials")
            {
                attempts.Set(key, failures + 1, FailureWindow);
                return Refused(StatusCodes.Status401Unauthorized, problem);
            }

            return Refused(problem == "no-email" ? StatusCodes.Status403Forbidden : StatusCodes.Status502BadGateway, problem!);
        }

        attempts.Remove(key);
        var externalId = "ldap|" + person.Id;
        var deactivated = await context.ExternalIdentities.AnyAsync(row => row.TenantId == found.Tenant && row.ExternalId == externalId && !row.Active, cancellationToken);

        if (deactivated || settings.RoleFor(person.Groups) is not { } role)
        {
            return Refused(StatusCodes.Status403Forbidden, "no-access");
        }

        var result = await externalUsers.ApplyAsync(found.Tenant, externalId, person.Email, person.Name, role, "ldap", null, cancellationToken);
        session.UserId = result.User.Id;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(await sessions.SignInAsync(result.User, found.Tenant, cancellationToken, external: true));
    }

    private async Task<SsoRow?> OptionsAsync(string organisation, CancellationToken cancellationToken)
    {
        if (organisation.Length is 0 or > 40)
        {
            return null;
        }

        var slug = organisation.Trim().ToLowerInvariant();

        return await context.Database.SqlQuery<SsoRow>($"SELECT tenant AS \"Tenant\", oidc AS \"Oidc\", ldap AS \"Ldap\", oidc_name AS \"OidcName\", ldap_name AS \"LdapName\" FROM sso_options({slug})").FirstOrDefaultAsync(cancellationToken);
    }

    private ObjectResult Refused(int status, string code) => StatusCode(status, new SignInProblem(code, null, null, Activity.Current?.TraceId.ToHexString()));

    /// <summary>A row of sso_options.</summary>
    public sealed record SsoRow(Guid Tenant, bool Oidc, bool Ldap, string OidcName, string LdapName);
}

/// <summary>How an organisation's people sign in.</summary>
/// <param name="Oidc">With single sign-on: go to /sso/{organisation}.</param>
/// <param name="Ldap">With their directory password: post to /api/app/session/ldap.</param>
/// <param name="OidcName">The provider's name for "Sign in with …".</param>
/// <param name="LdapName">The directory's name for "Sign in with …".</param>
public sealed record SingleSignOnOptions(bool Oidc, bool Ldap, string? OidcName, string? LdapName);

/// <summary>A sign-in with the directory.</summary>
/// <param name="Organisation">The organisation's short name.</param>
/// <param name="Username">The directory user name (the login attribute's value).</param>
/// <param name="Password">The directory password; never stored.</param>
public sealed record DirectorySignInRequest(string? Organisation, string? Username, string? Password);
