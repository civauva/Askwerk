// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.App.Jobs;
using Askwerk.Api.App.Surveys;
using Askwerk.Api.Identity;
using Askwerk.Api.Integrations;
using Askwerk.Application.Auditing;
using Askwerk.Application.Participants;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Integrations;
using Askwerk.Domain.Participants;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Public API: participants (W36, AWK-49), for a system that sends people. Each person is matched by External ID,
/// then email (or by email only when its connection says so): known people get the new details and keep their link,
/// new people are invited.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Route("api/v1/surveys/{surveyId:guid}/participants")]
[Produces("application/json")]
public sealed class PublicParticipantsController(
    AskwerkDbContext context,
    IInstanceSettingsRepository settings,
    EmailSettingsProvider email,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;
    private const int MaxPeople = 1000;

    /// <summary>Lists a survey's participants with their status and personal link, oldest first.</summary>
    /// <remarks>Scope participants:read. Pages on a cursor: pass nextCursor as cursor until it is null.</remarks>
    /// <param name="surveyId">The survey.</param>
    /// <param name="externalId">Only the person with this External ID.</param>
    /// <param name="limit">People per page, at most 100; 50 when empty.</param>
    /// <param name="cursor">Where the page starts, from the previous page's nextCursor.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of participants.</returns>
    /// <response code="200">One page of participants.</response>
    /// <response code="401">unauthorized: no valid API key or token.</response>
    /// <response code="403">scope-missing: it lacks participants:read.</response>
    /// <response code="404">survey-not-found.</response>
    [HttpGet]
    [Authorize(Policy = PublicApi.ParticipantsRead)]
    [ProducesResponseType<PublicParticipantPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> List(Guid surveyId, string? externalId, int? limit, Guid? cursor, CancellationToken cancellationToken)
    {
        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "survey-not-found");
        }

        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await context.Participants.AsNoTracking()
            .Where(row => row.SurveyId == surveyId && (externalId == null || row.ExternalId == externalId) && (cursor == null || row.Id.CompareTo(cursor.Value) > 0))
            .OrderBy(row => row.Id)
            .Take(size + 1)
            .Select(row => new { Person = row, Host = context.Invitations.Where(invitation => invitation.Id == row.InvitationId).Select(invitation => invitation.Host).FirstOrDefault(), Answered = context.Responses.Any(response => response.ParticipantId == row.Id && response.CompletedAt != null), Started = context.Responses.Any(response => response.ParticipantId == row.Id) })
            .ToListAsync(cancellationToken);
        var links = await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken);
        var page = rows.Take(size).ToList();

        return Ok(new PublicParticipantPage(
            [.. page.Select(row => new PublicParticipant(
                row.Person.Id,
                row.Person.Email,
                row.Person.Name,
                row.Person.ExternalId,
                PersonDetails.ReadFields(row.Person.Fields),
                row.Answered ? "answered" : row.Started ? "started" : row.Person.BouncedAt is not null ? "bounced" : row.Person.OpenedAt is not null ? "opened" : row.Person.SentAt is not null ? "sent" : "waiting",
                ParticipantsController.PersonalLink(links, row.Host, row.Person.Token)))],
            rows.Count > size ? page[^1].Person.Id.ToString() : null));
    }

    /// <summary>Adds or updates people and invites the new ones.</summary>
    /// <remarks>
    /// Scope participants:write. Known people (same External ID, else same email) get the new details and keep their
    /// link; unsubscribed addresses are skipped. send false makes personal links only, without emails.
    /// </remarks>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">At most 1000 people.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many were invited, updated and skipped, and each person's link.</returns>
    /// <response code="202">Invited; emails are on their way.</response>
    /// <response code="400">too-many-people or people-required.</response>
    /// <response code="401">unauthorized: no valid API key or token.</response>
    /// <response code="403">scope-missing: it lacks participants:write.</response>
    /// <response code="404">survey-not-found.</response>
    /// <response code="409">not-published or email-not-set-up.</response>
    [HttpPost]
    [Authorize(Policy = PublicApi.ParticipantsWrite)]
    [ProducesResponseType<PublicInvited>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Invite(Guid surveyId, PublicInviteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "survey-not-found");
        }

        var people = request.Participants ?? [];

        if (people.Count == 0 || people.Count > MaxPeople)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status400BadRequest, people.Count == 0 ? "people-required" : "too-many-people");
        }

        if (survey.PublishedVersion is null)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status409Conflict, "not-published");
        }

        var send = request.Send ?? true;

        if (send && await email.FindAsync(cancellationToken) is null)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status409Conflict, "email-not-set-up");
        }

        var (createdBy, matchBy, actorKind, actorId) = await CallerAsync(cancellationToken);
        var byExternalId = matchBy != Connection.MatchEmail;
        var now = time.GetUtcNow();
        var existing = await context.Participants.Where(row => row.SurveyId == surveyId).ToListAsync(cancellationToken);
        var unsubscribed = (await context.Unsubscribes.Select(row => row.Email).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var invitation = Invitation.Create(Guid.CreateVersion7(now), survey.TenantId, survey.Id, send, request.SendAt, request.Deadline, request.Reminder ?? true, null, createdBy, now);
        var added = new List<Participant>();
        var results = new List<Participant>();
        var updated = 0;
        var skipped = 0;

        foreach (var person in people)
        {
            var details = new ParticipantInput(person.Email?.Trim().ToLowerInvariant(), person.Name, person.Language, string.IsNullOrWhiteSpace(person.ExternalId) ? null : person.ExternalId.Trim(), person.Fields).Details();

            if (!ParticipantList.IsAddress(details.Email))
            {
                skipped++;
                continue;
            }

            var known = (byExternalId && details.ExternalId is { } id ? existing.Find(row => row.ExternalId == id) : null) ?? existing.Find(row => row.Email == details.Email);

            if (known is not null)
            {
                known.Change(new PersonDetails(details.Email, details.Name ?? known.Name, details.Language ?? known.Language, details.ExternalId ?? known.ExternalId, person.Fields ?? PersonDetails.ReadFields(known.Fields)));
                updated += results.Contains(known) ? 0 : 1;
                results.Add(known);
            }
            else if (unsubscribed.Contains(details.Email))
            {
                skipped++;
            }
            else
            {
                var participant = Participant.Add(Guid.CreateVersion7(now), survey.TenantId, survey.Id, invitation.Id, details, null, now);
                context.Participants.Add(participant);
                existing.Add(participant);
                added.Add(participant);
                results.Add(participant);
            }
        }

        if (added.Count > 0)
        {
            context.Invitations.Add(invitation);

            if (send)
            {
                JobRunner.Enqueue(context, survey.TenantId, ParticipantsController.SendInvitations, new SendInvitationsPayload(invitation.Id), invitation.SendAt, now);

                if (invitation.RemindAt is { } remindAt)
                {
                    JobRunner.Enqueue(context, survey.TenantId, ParticipantsController.SendReminders, new SendRemindersPayload(survey.Id, invitation.Id), remindAt, now);
                }
            }
        }

        await audit.WriteAsync(
            new AuditContent(now, survey.TenantId, actorKind, actorId, "participants.invited", "Survey", survey.Id.ToString(), $"{added.Count} invited, {updated} updated", trace.CurrentTraceId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var links = await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken);
        var hosts = await context.Invitations.AsNoTracking().Where(row => row.SurveyId == surveyId).ToDictionaryAsync(row => row.Id, row => row.Host, cancellationToken);

        return StatusCode(StatusCodes.Status202Accepted, new PublicInvited(
            added.Count,
            updated,
            skipped,
            [.. results.Distinct().Select(row => new PublicInvitedPerson(row.Id, row.Email, row.ExternalId, added.Contains(row) ? "invited" : "updated", ParticipantsController.PersonalLink(links, hosts.GetValueOrDefault(row.InvitationId), row.Token)))]));
    }

    private Task<Survey?> SurveyAsync(Guid surveyId, CancellationToken cancellationToken) =>
        context.Surveys.FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);

    /// <summary>Who calls: the key's or client's creator stands for the invitation; its connection says how to match.</summary>
    private async Task<(Guid CreatedBy, string? MatchBy, AuditActorKind Kind, string Id)> CallerAsync(CancellationToken cancellationToken)
    {
        if (User.OAuthClientId() is { } clientId)
        {
            var creator = await context.OAuthClients.Where(row => row.Id == clientId).Select(row => row.CreatedBy).FirstAsync(cancellationToken);
            var match = await context.Connections.Where(row => row.OAuthClientId == clientId).Select(row => row.MatchBy).FirstOrDefaultAsync(cancellationToken);

            return (creator, match, AuditActorKind.OAuthClient, clientId.ToString());
        }

        var keyId = User.ApiKeyId()!.Value;
        var keyCreator = await context.ApiKeys.Where(row => row.Id == keyId).Select(row => row.CreatedBy).FirstAsync(cancellationToken);
        var keyMatch = await context.Connections.Where(row => row.ApiKeyId == keyId).Select(row => row.MatchBy).FirstOrDefaultAsync(cancellationToken);

        return (keyCreator, keyMatch, AuditActorKind.ApiKey, keyId.ToString());
    }
}

/// <summary>A person from the other system.</summary>
/// <param name="Email">Their address.</param>
/// <param name="Name">Their name, for {name}.</param>
/// <param name="Language">Their language, e.g. de.</param>
/// <param name="ExternalId">Their id in the other system; finds them again when the address changes.</param>
/// <param name="Fields">Further fields, e.g. department; placeholders in messages.</param>
public sealed record PublicPerson(string? Email, string? Name = null, string? Language = null, string? ExternalId = null, IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>People to add or update.</summary>
/// <param name="Participants">At most 1000.</param>
/// <param name="Send">Send invitation emails to new people (default true); false makes links only.</param>
/// <param name="SendAt">When to send (UTC); now when empty.</param>
/// <param name="Deadline">"Answer by"; two weeks after sending when empty.</param>
/// <param name="Reminder">One reminder before the deadline (default true).</param>
public sealed record PublicInviteRequest(IReadOnlyList<PublicPerson>? Participants, bool? Send = null, DateTimeOffset? SendAt = null, DateOnly? Deadline = null, bool? Reminder = null);

/// <summary>A person after the call.</summary>
/// <param name="Id">The participant.</param>
/// <param name="Email">The address.</param>
/// <param name="ExternalId">Their id in the other system.</param>
/// <param name="Result">invited or updated.</param>
/// <param name="Link">Their personal link.</param>
public sealed record PublicInvitedPerson(Guid Id, string Email, string? ExternalId, string Result, string Link);

/// <summary>What the call did.</summary>
/// <param name="Invited">New people.</param>
/// <param name="Updated">Known people with new details.</param>
/// <param name="Skipped">Not an address, or unsubscribed.</param>
/// <param name="Participants">The invited and updated people.</param>
public sealed record PublicInvited(int Invited, int Updated, int Skipped, IReadOnlyList<PublicInvitedPerson> Participants);

/// <summary>A participant.</summary>
/// <param name="Id">The participant.</param>
/// <param name="Email">The address.</param>
/// <param name="Name">The name.</param>
/// <param name="ExternalId">Their id in the other system.</param>
/// <param name="Fields">Further fields.</param>
/// <param name="Status">waiting, sent, opened, started, answered or bounced.</param>
/// <param name="Link">Their personal link.</param>
public sealed record PublicParticipant(Guid Id, string Email, string? Name, string? ExternalId, IReadOnlyDictionary<string, string> Fields, string Status, string Link);

/// <summary>One page of participants.</summary>
/// <param name="Items">Oldest first.</param>
/// <param name="NextCursor">Pass as cursor for the next page; null on the last.</param>
public sealed record PublicParticipantPage(IReadOnlyList<PublicParticipant> Items, string? NextCursor);
