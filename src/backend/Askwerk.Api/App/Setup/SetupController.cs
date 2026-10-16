// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Configuration;
using Askwerk.Api.Identity;
using Askwerk.Application.Email;
using Askwerk.Application.Setup;
using Askwerk.Application.Storage;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Setup;

/// <summary>The first-run setup (AWK-16, W2). Open to anyone, but only until it has run once; then 404.</summary>
[ApiController]
[Route("api/app/setup")]
[ApiExplorerSettings(GroupName = "app")]
[AllowAnonymous]
public sealed partial class SetupController(IInstanceSettingsRepository settings, ITenantRepository tenants) : ControllerBase
{
    /// <summary>Whether setup still has to run.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 while setup is needed, otherwise 404.</returns>
    [HttpGet]
    [ProducesResponseType<SetupStateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SetupStateResponse>> Get(CancellationToken cancellationToken) =>
        await settings.FindAsync(cancellationToken) is null ? new SetupStateResponse(Required: true) : NotFound();

    /// <summary>Whether a slug is free, and a suggestion from the organisation name (step 3).</summary>
    /// <param name="name">Organisation name.</param>
    /// <param name="slug">Slug to check.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Availability and suggestion; 404 once setup has run.</returns>
    [HttpGet("slug")]
    [ProducesResponseType<SlugCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SlugCheckResponse>> CheckSlug([FromQuery] string? name, [FromQuery] string? slug, CancellationToken cancellationToken)
    {
        if (await settings.FindAsync(cancellationToken) is not null)
        {
            return NotFound();
        }

        var candidate = slug ?? "";
        var available = Tenant.IsValidSlug(candidate) && !await tenants.SlugTakenAsync(candidate, cancellationToken);

        return new SlugCheckResponse(available, Tenant.SuggestSlug(name ?? ""));
    }

    /// <summary>Sends a test email through the given server (step 4), only while setup has not run.</summary>
    /// <param name="request">Server, recipient and language.</param>
    /// <param name="sender">Sends the email.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 when the server accepted it; 400 with the server's answer; 404 once setup has run.</returns>
    [HttpPost("test-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestEmail(TestEmailRequest request, [FromServices] IEmailSender sender, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await settings.FindAsync(cancellationToken) is not null)
        {
            return NotFound();
        }

        try
        {
            await sender.SendAsync(request.Smtp.ToSettings(), EmailTexts.Test(request.To, request.Language), cancellationToken);
        }
        catch (Exception error) when (error is System.Net.Mail.SmtpException or FormatException or InvalidOperationException)
        {
            ModelState.AddModelError("smtp", "smtp-failed");
            return ValidationProblem(ModelState);
        }

        return NoContent();
    }

    /// <summary>Runs setup and signs the new owner in; two-factor setup follows (required for owners).</summary>
    /// <param name="request">The setup input.</param>
    /// <param name="handler">The use case.</param>
    /// <param name="sessions">Signs the owner in.</param>
    /// <param name="users">Finds the new owner.</param>
    /// <param name="askwerk">Start settings: whether to seed the demo survey (AWK-23).</param>
    /// <param name="time">The clock.</param>
    /// <param name="storage">File storage, for the demo survey's picture.</param>
    /// <param name="logger">Logs a demo survey that could not be created.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the session state; 400 at the field; 404 once setup has run.</returns>
    [HttpPost]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionResponse>> Post(
        SetupRequest request,
        [FromServices] CompleteSetupHandler handler,
        [FromServices] SessionService sessions,
        [FromServices] UserManager<User> users,
        [FromServices] AskwerkSettings askwerk,
        [FromServices] TimeProvider time,
        [FromServices] IFileStorage storage,
        [FromServices] ILogger<SetupController> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SetupResult result;

        try
        {
            result = await handler.HandleAsync(
                new CompleteSetupCommand(
                    request.Owner.Name,
                    request.Owner.Email,
                    request.Owner.Password,
                    request.Instance.Name,
                    request.Instance.BaseAddress,
                    request.Instance.DefaultLanguage,
                    request.Instance.TimeZone,
                    request.Organisation.Name,
                    request.Organisation.Slug,
                    request.Email?.ToSettings()),
                cancellationToken);
        }
        catch (SetupAlreadyCompletedException)
        {
            return NotFound();
        }

        // The new organisation gets the demo survey right away (AWK-23); a failure never fails the setup.
        if (askwerk.DemoSurvey)
        {
            try
            {
                await DemoSurvey.EnsureAsync(askwerk.OwnerConnectionString, storage, time, System.Diagnostics.Activity.Current?.TraceId.ToHexString(), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDemoSurveyFailed(logger, exception.GetType().Name, exception.Message);
            }
        }

        var owner = await users.FindByIdAsync(result.UserId.ToString());
        var state = await sessions.SignInAsync(owner!, result.TenantId, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, state);
    }

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Demo survey not created after setup ({ErrorType}: {ErrorMessage}).")]
    private static partial void LogDemoSurveyFailed(ILogger logger, string errorType, string errorMessage);
}

/// <summary>The test email of step 4.</summary>
/// <param name="Smtp">The server to try.</param>
/// <param name="To">Recipient, usually the owner.</param>
/// <param name="Language">de or en.</param>
public sealed record TestEmailRequest(SetupEmail Smtp, string To, string Language);

/// <summary>SMTP settings of step 4.</summary>
/// <param name="Host">Host.</param>
/// <param name="Port">Port.</param>
/// <param name="UserName">Login, if any.</param>
/// <param name="Password">Password, if any.</param>
/// <param name="Sender">From address.</param>
public sealed record SetupEmail(string Host, int Port, string? UserName, string? Password, string Sender)
{
    /// <summary>As the application's settings.</summary>
    /// <returns>The settings.</returns>
    public SmtpSettings ToSettings() => new(Host, Port, UserName, Password, Sender);
}

/// <summary>Whether setup is needed.</summary>
/// <param name="Required">Always true; once setup has run the endpoint answers 404.</param>
public sealed record SetupStateResponse(bool Required);

/// <summary>Result of a slug check.</summary>
/// <param name="Available">Whether the slug is valid and unused.</param>
/// <param name="Suggestion">A slug derived from the name.</param>
public sealed record SlugCheckResponse(bool Available, string Suggestion);

/// <summary>Setup input (W2 steps 1–3).</summary>
/// <param name="Owner">The owner's account.</param>
/// <param name="Instance">The installation.</param>
/// <param name="Organisation">The first organisation.</param>
/// <param name="Email">SMTP settings, or null when step 4 was skipped.</param>
public sealed record SetupRequest(SetupOwner Owner, SetupInstance Instance, SetupOrganisation Organisation, SetupEmail? Email = null);

/// <summary>The owner's account.</summary>
/// <param name="Name">Name.</param>
/// <param name="Email">Email.</param>
/// <param name="Password">Password.</param>
public sealed record SetupOwner(string Name, string Email, string Password);

/// <summary>The installation.</summary>
/// <param name="Name">Instance name.</param>
/// <param name="BaseAddress">Base address.</param>
/// <param name="DefaultLanguage">Default language.</param>
/// <param name="TimeZone">IANA time zone.</param>
public sealed record SetupInstance(string Name, string BaseAddress, string DefaultLanguage, string TimeZone);

/// <summary>The first organisation.</summary>
/// <param name="Name">Name.</param>
/// <param name="Slug">Short name.</param>
public sealed record SetupOrganisation(string Name, string Slug);
