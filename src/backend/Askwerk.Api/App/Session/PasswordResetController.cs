// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Identity;
using Askwerk.Application.Email;
using Askwerk.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Session;

/// <summary>Password reset by email link (AWK-16, W1 E): the link works once, for 30 minutes. Answers never reveal accounts.</summary>
[ApiController]
[Route("api/app/password-reset")]
[ApiExplorerSettings(GroupName = "app")]
[AllowAnonymous]
public sealed class PasswordResetController(UserManager<User> users, EmailSettingsProvider email, IEmailSender sender, AccountAudit audit) : ControllerBase
{
    /// <summary>Whether reset by email is possible (only with email settings).</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Availability.</returns>
    [HttpGet]
    [ProducesResponseType<PasswordResetStateResponse>(StatusCodes.Status200OK)]
    public async Task<PasswordResetStateResponse> State(CancellationToken cancellationToken) =>
        new(Available: await email.FindAsync(cancellationToken) is not null);

    /// <summary>Sends a reset link if the address has an account and email is set up. Always 202.</summary>
    /// <param name="request">The email address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendLink(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await email.FindAsync(cancellationToken);
        var user = await users.FindByEmailAsync(request.Email.Trim());

        if (settings is { } found && user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var link = $"{found.BaseAddress}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";
            await sender.SendAsync(found.Smtp, EmailTexts.PasswordReset(user.Email, link, found.Language), cancellationToken);
            await audit.WriteAsync(user, "user.password-reset-requested", cancellationToken);
        }

        return Accepted();
    }

    /// <summary>Sets the new password with the token from the link; afterwards the token no longer works.</summary>
    /// <param name="request">Email, token and new password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at the token or the password.</returns>
    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm(PasswordResetConfirmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        var result = user is null ? IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }) : await users.ResetPasswordAsync(user, request.Token, request.Password);

        if (!result.Succeeded)
        {
            var error = result.Errors.First().Code;
            ModelState.AddModelError(error == "InvalidToken" ? "token" : "password", error == "InvalidToken" ? "token-invalid" : IdentityCodes.ToApiCode(error));
            return ValidationProblem(ModelState);
        }

        await users.ResetAccessFailedCountAsync(user!);
        await users.SetLockoutEndDateAsync(user!, null);
        await audit.WriteAsync(user!, "user.password-reset", cancellationToken);

        return NoContent();
    }
}

/// <summary>Whether reset by email is possible.</summary>
/// <param name="Available">True with email settings.</param>
public sealed record PasswordResetStateResponse(bool Available);

/// <summary>The address to send the link to.</summary>
/// <param name="Email">Email.</param>
public sealed record PasswordResetRequest(string Email);

/// <summary>The new password with the token from the link.</summary>
/// <param name="Email">Email.</param>
/// <param name="Token">Token from the link.</param>
/// <param name="Password">New password.</param>
public sealed record PasswordResetConfirmRequest(string Email, string Token, string Password);
