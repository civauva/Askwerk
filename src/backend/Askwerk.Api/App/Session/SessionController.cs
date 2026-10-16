// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using Askwerk.Api.Identity;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Session;

/// <summary>Sign-in and sign-out with Askwerk's own accounts (AWK-16, W1): password, then the authenticator code if two-factor is on.</summary>
[ApiController]
[Route("api/app/session")]
[ApiExplorerSettings(GroupName = "app")]
public sealed class SessionController(
    SignInManager<User> signIn,
    UserManager<User> users,
    SessionService sessions,
    AccountAudit audit,
    EmailSettingsProvider email,
    Askwerk.Application.Email.IEmailSender sender) : ControllerBase
{
    /// <summary>Checks email and password. Never reveals whether an email has an account.</summary>
    /// <param name="request">Email and password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 401 with the tries left; 403 while locked.</returns>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SignInProblem>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<SignInProblem>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SessionResponse>> SignIn(SignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.FindByEmailAsync(request.Email.Trim());

        if (user is null)
        {
            return Refused(StatusCodes.Status401Unauthorized, "invalid-credentials", triesLeft: null, lockedUntil: null);
        }

        var wasLocked = await users.IsLockedOutAsync(user);
        var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (check.IsLockedOut)
        {
            if (!wasLocked)
            {
                // This attempt caused the pause: tell the account owner, in case it was not them.
                await audit.WriteAsync(user, "user.locked-out", cancellationToken);

                if (await email.FindAsync(cancellationToken) is { } settings)
                {
                    await sender.SendAsync(settings.Smtp, EmailTexts.Lockout(user.Email, user.LockoutEnd!.Value, settings.Language), cancellationToken);
                }
            }

            return Refused(StatusCodes.Status403Forbidden, "locked", triesLeft: null, lockedUntil: user.LockoutEnd);
        }

        if (!check.Succeeded)
        {
            await audit.WriteAsync(user, "user.sign-in-failed", cancellationToken);
            var triesLeft = users.Options.Lockout.MaxFailedAccessAttempts - user.AccessFailedCount;
            return Refused(StatusCodes.Status401Unauthorized, "invalid-credentials", triesLeft, lockedUntil: null);
        }

        // A browser trusted for 30 days skips the code; Identity's PasswordSignInAsync would sign in with its default claims instead.
        if (user.TwoFactorEnabled && !await signIn.IsTwoFactorClientRememberedAsync(user))
        {
            // Sets Identity's short-lived two-factor cookie; the session itself starts after the code.
            await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: false);
            return new SessionResponse("two-factor-required");
        }

        if (await sessions.PasskeyRequiredAsync(user, cancellationToken))
        {
            return new SessionResponse("passkey-required");
        }

        await audit.WriteAsync(user, "user.signed-in", cancellationToken);
        return await sessions.SignInAsync(user, preferredTenant: null, cancellationToken);
    }

    /// <summary>Options for navigator.credentials.get(): any passkey of this site (discoverable credentials).</summary>
    /// <returns>The request options as JSON.</returns>
    [HttpPost("passkey/options")]
    [AllowAnonymous]
    [Produces("application/json")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    public async Task<ContentResult> PasskeyOptions() =>
        Content(await signIn.MakePasskeyRequestOptionsAsync(user: null), "application/json");

    /// <summary>Signs in with a passkey; it replaces both password and app code.</summary>
    /// <param name="request">The credential from navigator.credentials.get(), as JSON.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 401 when the passkey is unknown or the signature does not verify.</returns>
    [HttpPost("passkey")]
    [AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SignInProblem>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> PasskeySignIn(PasskeyCredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await signIn.PerformPasskeyAssertionAsync(request.Credential);

        if (!result.Succeeded || result.User.DeactivatedAt is not null)
        {
            return Refused(StatusCodes.Status401Unauthorized, "passkey-invalid", triesLeft: null, lockedUntil: null);
        }

        // Stores the new signature counter and the time of use.
        await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey);
        await audit.WriteAsync(result.User, "user.signed-in-passkey", cancellationToken);

        return await sessions.SignInAsync(result.User, preferredTenant: null, cancellationToken);
    }

    /// <summary>Second step: the code from the authenticator app.</summary>
    /// <param name="request">The code and whether to trust this browser for 30 days.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 400 at the code; 401 without a password step.</returns>
    [HttpPost("two-factor")]
    [AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> TwoFactor(TwoFactorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return Unauthorized();
        }

        var result = await signIn.TwoFactorAuthenticatorSignInAsync(request.Code.Replace(" ", "", StringComparison.Ordinal), isPersistent: false, rememberClient: request.RememberBrowser);

        return await CompleteSecondStepAsync(user, result, cancellationToken);
    }

    /// <summary>Second step with a recovery code instead of the app; each code works once.</summary>
    /// <param name="request">The recovery code.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 400 at the code; 401 without a password step.</returns>
    [HttpPost("recovery")]
    [AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> Recovery(RecoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return Unauthorized();
        }

        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.Code.Trim());

        return await CompleteSecondStepAsync(user, result, cancellationToken);
    }

    /// <summary>Works in another organisation the user belongs to.</summary>
    /// <param name="request">The organisation.</param>
    /// <param name="context">The database, scoped to the signed-in user.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The session state; 403 when the user is not a member.</returns>
    [HttpPut("tenant")]
    [Authorize(Policy = Sessions.SignedIn)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SessionResponse>> SwitchTenant(SwitchTenantRequest request, [FromServices] AskwerkDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = User.UserId();
        var member = await context.Memberships.AnyAsync(membership => membership.UserId == userId && membership.TenantId == request.TenantId, cancellationToken)
            && await context.Tenants.AnyAsync(tenant => tenant.Id == request.TenantId && tenant.Status == Askwerk.Domain.Identity.TenantStatus.Active, cancellationToken);

        // Only members switch, and only into an organisation that works (AWK-38).
        if (!member)
        {
            return Forbid();
        }

        var user = await users.GetUserAsync(User);

        return await sessions.SignInAsync(user!, request.TenantId, cancellationToken);
    }

    /// <summary>Ends the session in this browser.</summary>
    /// <returns>204, also when nobody was signed in.</returns>
    [HttpDelete]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EndSession()
    {
        await signIn.SignOutAsync();

        return NoContent();
    }

    private async Task<ActionResult<SessionResponse>> CompleteSecondStepAsync(User user, Microsoft.AspNetCore.Identity.SignInResult result, CancellationToken cancellationToken)
    {
        if (result.IsLockedOut)
        {
            return Refused(StatusCodes.Status403Forbidden, "locked", triesLeft: null, lockedUntil: user.LockoutEnd);
        }

        if (!result.Succeeded)
        {
            await audit.WriteAsync(user, "user.two-factor-failed", cancellationToken);
            ModelState.AddModelError("code", "code-invalid");
            return ValidationProblem(ModelState);
        }

        await audit.WriteAsync(user, "user.signed-in", cancellationToken);

        // Identity signed in with its default claims; sign in again with Askwerk's (tenant, pending steps).
        return await sessions.SignInAsync(user, preferredTenant: null, cancellationToken);
    }

    private ObjectResult Refused(int status, string code, int? triesLeft, DateTimeOffset? lockedUntil) =>
        StatusCode(status, new SignInProblem(code, triesLeft, lockedUntil, Activity.Current?.TraceId.ToHexString()));
}

/// <summary>A WebAuthn credential as the browser's PublicKeyCredential.toJSON() produces it.</summary>
/// <param name="Credential">The credential JSON.</param>
public sealed record PasskeyCredentialRequest(string Credential);

/// <summary>Email and password.</summary>
/// <param name="Email">Email.</param>
/// <param name="Password">Password.</param>
public sealed record SignInRequest(string Email, string Password);

/// <summary>The authenticator code.</summary>
/// <param name="Code">Six digits.</param>
/// <param name="RememberBrowser">Skip the code in this browser for 30 days.</param>
public sealed record TwoFactorRequest(string Code, bool RememberBrowser);

/// <summary>A recovery code.</summary>
/// <param name="Code">The code.</param>
public sealed record RecoveryRequest(string Code);

/// <summary>The organisation to work in.</summary>
/// <param name="TenantId">Its id.</param>
public sealed record SwitchTenantRequest(Guid TenantId);

/// <summary>Why a sign-in was refused.</summary>
/// <param name="Code"><c>invalid-credentials</c> or <c>locked</c>.</param>
/// <param name="TriesLeft">Failed tries left before the pause.</param>
/// <param name="LockedUntil">When signing in is possible again.</param>
/// <param name="TraceId">Reference for support.</param>
public sealed record SignInProblem(string Code, int? TriesLeft, DateTimeOffset? LockedUntil, string? TraceId);
