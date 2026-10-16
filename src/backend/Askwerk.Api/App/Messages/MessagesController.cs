// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Askwerk.Api.App.Themes;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Email;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Messages;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Messages;

/// <summary>
/// Message texts of the organisation (AWK-17 step 6, W10 D): invitation, reminder and thanks, per language, with a
/// preview drawn by <see cref="EmailRenderer"/> and a test send to the signed-in user. Sending to respondents comes with
/// distribution (AWK-7).
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/messages")]
public sealed class MessagesController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Changers = [TenantRole.Owner, TenantRole.Admin];
    private static readonly MessageKind[] Kinds = [MessageKind.Invitation, MessageKind.Reminder, MessageKind.Thanks];

    /// <summary>The three message texts; Askwerk's own where the organisation has none.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The texts.</returns>
    [HttpGet]
    [ProducesResponseType<MessageListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MessageListResponse>> List(CancellationToken cancellationToken)
    {
        var own = await context.MessageTemplates.ToListAsync(cancellationToken);

        return new MessageListResponse([.. Kinds.Select(kind =>
        {
            var stored = own.FirstOrDefault(template => template.Kind == kind);
            var (subject, body) = DefaultMessages.For(kind);

            return stored is null
                ? new MessageResponse(Name(kind), subject, body, kind == MessageKind.Invitation, true)
                : new MessageResponse(Name(kind), stored.Subject, stored.Body, stored.AnswerInEmail, false);
        })]);
    }

    /// <summary>Saves the organisation's text for one kind of message.</summary>
    /// <param name="kind">invitation, reminder or thanks.</param>
    /// <param name="request">Subject and text per language.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 naming an unknown placeholder; 403 for other roles; 404 for an unknown kind.</returns>
    [HttpPut("{kind}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save(string kind, SaveMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Parse(kind) is not { } messageKind)
        {
            return NotFound();
        }

        if (User.TenantId() is not { } tenantId || !await MayChangeAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        var subject = request.Subject ?? new TranslatedText(new Dictionary<string, string>());
        var body = request.Body ?? new TranslatedText(new Dictionary<string, string>());

        foreach (var (field, text) in new[] { ("subject", subject), ("body", body) })
        {
            foreach (var (language, value) in text)
            {
                if (Placeholders.Unknown(value).Any())
                {
                    ModelState.AddModelError($"{field}.{language}", "placeholder-unknown");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var template = await context.MessageTemplates.FirstOrDefaultAsync(row => row.Kind == messageKind, cancellationToken);

        if (template is null)
        {
            template = MessageTemplate.Create(Guid.CreateVersion7(now), tenantId, messageKind, subject, body, request.AnswerInEmail, now);
            context.MessageTemplates.Add(template);
        }
        else
        {
            template.Change(subject, body, request.AnswerInEmail, now);
        }

        await audit.WriteAsync(new AuditContent(now, tenantId, AuditActorKind.User, User.UserId().ToString()!, "message.saved", "MessageTemplate", template.Id.ToString(), Diff: null, trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>The email as a respondent of the survey would get it; unsaved texts can be previewed too.</summary>
    /// <param name="kind">invitation, reminder or thanks.</param>
    /// <param name="request">The survey, the language and, optionally, texts not saved yet.</param>
    /// <param name="settings">Instance settings, for the base address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Subject, HTML and plain text; 404 for an unknown kind or survey.</returns>
    [HttpPost("{kind}/preview")]
    [ProducesResponseType<EmailPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmailPreviewResponse>> Preview(string kind, EmailRequest request, [FromServices] Askwerk.Application.Setup.IInstanceSettingsRepository settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        var baseAddress = (await settings.FindAsync(cancellationToken))?.BaseAddress.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var email = Parse(kind) is { } messageKind ? await ComposeAsync(messageKind, request, baseAddress, cancellationToken) : null;

        return email is null ? NotFound() : email;
    }

    /// <summary>Sends the email once, marked as a test, to the signed-in user only.</summary>
    /// <param name="kind">invitation, reminder or thanks.</param>
    /// <param name="request">The survey and the language.</param>
    /// <param name="email">The instance's SMTP settings.</param>
    /// <param name="sender">Sends email.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 409 while email is not set up; 404 for an unknown kind or survey.</returns>
    [HttpPost("{kind}/test")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<MessageProblem>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Test(string kind, EmailRequest request, [FromServices] EmailSettingsProvider email, [FromServices] IEmailSender sender, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(sender);

        if (await email.FindAsync(cancellationToken) is not var (smtp, baseAddress, _))
        {
            return Conflict(new MessageProblem("email-not-set-up", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var composed = Parse(kind) is { } messageKind ? await ComposeAsync(messageKind, request, baseAddress.TrimEnd('/'), cancellationToken) : null;

        if (composed is null)
        {
            return NotFound();
        }

        var userId = User.UserId();
        var to = await context.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync(cancellationToken);
        await sender.SendAsync(smtp, new EmailMessage(to!, $"[Test] {composed.Subject}", composed.Text, composed.Html), cancellationToken);

        return NoContent();
    }

    private static MessageKind? Parse(string kind) => kind switch
    {
        "invitation" => MessageKind.Invitation,
        "reminder" => MessageKind.Reminder,
        "thanks" => MessageKind.Thanks,
        _ => null,
    };

    private static string Name(MessageKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>Subject, HTML and text for the survey in the language, as the signed-in user would get it; null when the survey is not found.</summary>
    private async Task<EmailPreviewResponse?> ComposeAsync(MessageKind kind, EmailRequest request, string baseAddress, CancellationToken cancellationToken)
    {
        var survey = await context.Surveys.FirstOrDefaultAsync(row => row.Id == request.SurveyId && row.DeletedAt == null, cancellationToken);

        if (survey is null)
        {
            return null;
        }

        var userId = User.UserId();
        var name = await context.Users.Where(user => user.Id == userId).Select(user => user.DisplayName).SingleAsync(cancellationToken);
        var link = await Surveys.SurveyLinks.PrimaryAsync(context, HttpContext.RequestServices.GetRequiredService<Askwerk.Application.Setup.IInstanceSettingsRepository>(), Request, survey, cancellationToken);

        return await MessageComposer.ComposeAsync(
            context,
            baseAddress,
            new MessageFor(kind, survey, request.Language, name ?? "", link, DateOnly.FromDateTime(time.GetUtcNow().AddDays(14).UtcDateTime), Unsubscribe: null, request.Subject, request.Body, request.AnswerInEmail),
            cancellationToken);
    }

    private async Task<bool> MayChangeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Changers.Contains(known);
    }
}

/// <summary>The organisation's message texts.</summary>
/// <param name="Items">Invitation, reminder, thanks.</param>
public sealed record MessageListResponse(IReadOnlyList<MessageResponse> Items);

/// <summary>One message text.</summary>
/// <param name="Kind">invitation, reminder or thanks.</param>
/// <param name="Subject">Subject per language.</param>
/// <param name="Body">Text per language.</param>
/// <param name="AnswerInEmail">First question answerable in the email.</param>
/// <param name="IsDefault">Askwerk's text, not changed by the organisation.</param>
public sealed record MessageResponse(string Kind, TranslatedText Subject, TranslatedText Body, bool AnswerInEmail, bool IsDefault);

/// <summary>A message text to save.</summary>
/// <param name="Subject">Subject per language.</param>
/// <param name="Body">Text per language.</param>
/// <param name="AnswerInEmail">First question answerable in the email.</param>
public sealed record SaveMessageRequest(TranslatedText? Subject, TranslatedText? Body, bool AnswerInEmail);

/// <summary>Which email to draw: the survey, the language, and optionally texts not saved yet.</summary>
/// <param name="SurveyId">The survey the email is about.</param>
/// <param name="Language">The recipient's language; the survey's default when it does not offer it.</param>
/// <param name="Subject">Unsaved subject, for the preview.</param>
/// <param name="Body">Unsaved text, for the preview.</param>
/// <param name="AnswerInEmail">Unsaved switch, for the preview.</param>
public sealed record EmailRequest(Guid SurveyId, string? Language, TranslatedText? Subject = null, TranslatedText? Body = null, bool? AnswerInEmail = null);

/// <summary>An email as drawn.</summary>
/// <param name="Subject">Subject.</param>
/// <param name="Html">HTML part.</param>
/// <param name="Text">Plain-text part.</param>
public sealed record EmailPreviewResponse(string Subject, string Html, string Text);

/// <summary>Why a message was not sent.</summary>
/// <param name="Code">email-not-set-up.</param>
/// <param name="TraceId">Reference for support.</param>
public sealed record MessageProblem(string Code, string? TraceId);
