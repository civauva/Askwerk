// SPDX-License-Identifier: AGPL-3.0-only
using System.Linq.Expressions;
using System.Text;
using Askwerk.Api.App.Jobs;
using Askwerk.Api.App.Surveys;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Participants;
using Askwerk.Application.Persistence;
using Askwerk.Application.Setup;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Participants;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Invitations (AWK-7, W12 C and D): who to invite, from pasted lines or a file; sending with one reminder, as
/// background jobs; and who got what. Owners, admins and editors invite; everyone in the organisation sees the list.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys/{surveyId:guid}")]
public sealed class ParticipantsController(
    AskwerkDbContext context,
    IInstanceSettingsRepository settings,
    EmailSettingsProvider email,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    /// <summary>Sending kinds of the job table.</summary>
    public const string SendInvitations = "send-invitations";

    /// <summary>Sending kinds of the job table.</summary>
    public const string SendReminders = "send-reminders";

    private const int MaxFileBytes = 5 * 1024 * 1024;
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;
    private static readonly TenantRole[] Inviters = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];

    /// <summary>Reads pasted lines and says who would be invited (W12 C step 1).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">One person per line: an address, or Name &lt;address&gt;.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>New people, and how many are duplicates, already invited, unsubscribed or not addresses.</returns>
    [HttpPost("participants/check")]
    [Consumes("application/json")]
    [ProducesResponseType<ParticipantCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParticipantCheckResponse>> CheckText(Guid surveyId, ParticipantCheckRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await SurveyAsync(surveyId, cancellationToken) is null ? NotFound() : await CheckAsync(surveyId, ParticipantList.FromText(request.Text), cancellationToken);
    }

    /// <summary>Reads a CSV or Excel file with a header row (email, name, language) and says who would be invited.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="file">The list, at most 5 MB.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>As for pasted lines; 400 when the file has no email column.</returns>
    [HttpPost("participants/check")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ParticipantCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParticipantCheckResponse>> CheckFile(Guid surveyId, IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (await SurveyAsync(surveyId, cancellationToken) is null)
        {
            return NotFound();
        }

        if (file.Length > MaxFileBytes)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["file-too-large"] }));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        return ParticipantList.FromFile(file.FileName, buffer.ToArray()) is { } list
            ? await CheckAsync(surveyId, list, cancellationToken)
            : ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["no-email-column"] }));
    }

    /// <summary>
    /// Invites people (W12 C step 2): personal links sent at a time, with the deadline and one reminder to those
    /// who have not answered; or, while email is not set up, personal links only.
    /// </summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">The people and how to invite them.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202 with how many were invited and skipped; 403 for analysts and viewers; 404; 409 not-published or email-not-set-up.</returns>
    [HttpPost("invitations")]
    [ProducesResponseType<InvitedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite(Guid surveyId, InviteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        if (await RefusedAsync(survey.TenantId, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (survey.PublishedVersion is null)
        {
            return Problem(StatusCodes.Status409Conflict, "not-published");
        }

        var send = request.Send ?? true;

        if (send && await email.FindAsync(cancellationToken) is null)
        {
            return Problem(StatusCodes.Status409Conflict, "email-not-set-up");
        }

        // From the address book (W35): the chosen contacts, with their fields.
        var inputs = (request.Participants ?? []).ToList();

        if (request.ContactIds is { Count: > 0 } contactIds)
        {
            inputs.AddRange((await context.Contacts.Where(row => contactIds.Contains(row.Id)).ToListAsync(cancellationToken))
                .Select(contact => new ParticipantInput(contact.Email, contact.Name, contact.Language, contact.ExternalId, PersonDetails.ReadFields(contact.Fields))));
        }

        var check = await CheckPeopleAsync(surveyId, inputs, cancellationToken);
        var now = time.GetUtcNow();
        var invitation = Invitation.Create(Guid.CreateVersion7(now), survey.TenantId, survey.Id, send, request.SendAt, request.Deadline, request.Reminder ?? true, request.Host, User.UserId()!.Value, now);
        context.Invitations.Add(invitation);
        var contacts = await ContactBook.MatchAsync(context, check.Participants, cancellationToken);

        foreach (var person in check.Participants)
        {
            var details = person.Details();
            var contact = contacts.Find(details);

            if (contact is null && request.SaveToContacts == true)
            {
                contact = Contact.Create(Guid.CreateVersion7(now), survey.TenantId, details, request.ContactGroup is { } group ? [group] : null, now);
                context.Contacts.Add(contact);
                contacts.Add(contact);
            }
            else if (contact is not null && request.SaveToContacts == true)
            {
                contact.Merge(details, now);
                contact.Join(request.ContactGroup is { } group ? [group] : [], now);
            }

            context.Participants.Add(Participant.Add(Guid.CreateVersion7(now), survey.TenantId, survey.Id, invitation.Id, details, contact?.Id, now));
        }

        if (send && check.Participants.Count > 0)
        {
            JobRunner.Enqueue(context, survey.TenantId, SendInvitations, new SendInvitationsPayload(invitation.Id), invitation.SendAt, now);

            if (invitation.RemindAt is { } remindAt)
            {
                JobRunner.Enqueue(context, survey.TenantId, SendReminders, new SendRemindersPayload(survey.Id, invitation.Id), remindAt, now);
            }
        }

        await audit.WriteAsync(
            new AuditContent(now, survey.TenantId, AuditActorKind.User, User.UserId().ToString(), "participants.invited", "Survey", survey.Id.ToString(), $"{check.Participants.Count} invited", trace.CurrentTraceId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var skipped = inputs.Count - check.Participants.Count;

        return StatusCode(StatusCodes.Status202Accepted, new InvitedResponse(check.Participants.Count, skipped));
    }

    /// <summary>Who was invited and what happened (W12 D, W13 C), a page at a time; newest first unless sorted.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="sort">name, status or sent; a leading minus sorts descending.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="limit">Rows per page, at most 100; 50 when empty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="search">Part of the name, email or External ID (W35).</param>
    /// <param name="status">One status, or not-answered for all who have not answered (W35).</param>
    /// <param name="field">A field and its value, key:value (W35).</param>
    /// <returns>One page of participants with their status and personal link, the number of all, the counts, and the next planned reminder.</returns>
    [HttpGet("participants")]
    [ProducesResponseType<ParticipantListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParticipantListResponse>> List(Guid surveyId, string? sort, int offset, int? limit, CancellationToken cancellationToken, string? search = null, string? status = null, string? field = null)
    {
        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        // The status is worked out in the database, so the list can sort and count by it.
        var rows = context.Participants
            .Where(row => row.SurveyId == surveyId)
            .Join(context.Invitations, participant => participant.InvitationId, invitation => invitation.Id, (participant, invitation) => new { participant, invitation.Send, invitation.Host })
            .Select(row => new ListedParticipant
            {
                Participant = row.participant,
                Host = row.Host,
                Rank = context.Unsubscribes.Any(unsubscribe => unsubscribe.Email == row.participant.Email) ? 7
                : row.participant.BouncedAt != null ? 6
                : context.Responses.Any(response => response.ParticipantId == row.participant.Id && response.Status == ResponseStatus.Complete) ? 5
                : context.Responses.Any(response => response.ParticipantId == row.participant.Id) ? 4
                : row.participant.OpenedAt != null ? 3
                : row.participant.SentAt != null ? 2
                : row.Send ? 0
                : 1,
            });
        var counts = await rows.GroupBy(row => row.Rank).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var shown = Filtered(rows, search, status, field);
        var matching = await shown.CountAsync(cancellationToken);
        var page = await Sorted(shown, sort).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit)).ToListAsync(cancellationToken);
        var pageIds = page.Select(row => row.Participant.Id).ToList();
        var activity = await context.Responses
            .Where(response => response.ParticipantId != null && pageIds.Contains(response.ParticipantId.Value))
            .GroupBy(response => response.ParticipantId!.Value)
            .Select(group => new { Id = group.Key, At = group.Max(response => response.UpdatedAt) })
            .ToDictionaryAsync(row => row.Id, row => row.At, cancellationToken);
        var fieldKeys = await context.Database
            .SqlQuery<string>($"SELECT DISTINCT jsonb_object_keys(fields) AS \"Value\" FROM participant WHERE survey_id = {surveyId} ORDER BY 1")
            .ToListAsync(cancellationToken);
        var links = await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken);
        var now = time.GetUtcNow();
        var reminderAt = await context.Invitations.Where(row => row.SurveyId == surveyId && row.RemindAt > now).MinAsync(row => row.RemindAt, cancellationToken);

        return new ParticipantListResponse(
            [.. page.Select(row => new ParticipantItem(
                row.Participant.Id,
                row.Participant.Email,
                row.Participant.Name,
                row.Participant.Language,
                Statuses[row.Rank],
                row.Participant.SentAt,
                row.Participant.Reminders,
                PersonalLink(links, row.Host, row.Participant.Token),
                row.Participant.ExternalId,
                PersonDetails.ReadFields(row.Participant.Fields),
                new[] { activity.GetValueOrDefault(row.Participant.Id), row.Participant.OpenedAt, row.Participant.RemindedAt, row.Participant.SentAt, (DateTimeOffset?)row.Participant.CreatedAt }.Max(),
                row.Participant.RemindedAt,
                row.Participant.RemindersOff,
                row.Participant.ContactId))],
            counts.Sum(count => count.Count),
            counts.ToDictionary(count => Statuses[count.Key], count => count.Count),
            reminderAt,
            matching,
            fieldKeys,
            await AutoReminders.SettingsAsync(context, survey, time.GetUtcNow(), cancellationToken));
    }

    /// <summary>Reminds those invited who have not answered, now (W12 D: Send reminder now; W13 C: the chosen ones).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="selection">The chosen participants; everyone when empty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202; 403; 404; 409 email-not-set-up.</returns>
    [HttpPost("participants/remind")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remind(Guid surveyId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ParticipantSelection? selection, CancellationToken cancellationToken)
    {
        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        if (await RefusedAsync(survey.TenantId, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await email.FindAsync(cancellationToken) is null)
        {
            return Problem(StatusCodes.Status409Conflict, "email-not-set-up");
        }

        var now = time.GetUtcNow();
        var chosen = selection is { All: not true, ParticipantIds: { } ids } ? ids : null;
        JobRunner.Enqueue(context, survey.TenantId, SendReminders, new SendRemindersPayload(survey.Id, InvitationId: null, chosen), now, now);
        await audit.WriteAsync(
            new AuditContent(now, survey.TenantId, AuditActorKind.User, User.UserId().ToString(), "participants.reminded", "Survey", survey.Id.ToString(), chosen is null ? null : $"{chosen.Count} chosen", trace.CurrentTraceId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Removes the chosen participants, or all (W13 C): their personal links stop working and they get no more
    /// emails. People who started or sent a response are kept, so their response keeps its person.
    /// </summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="selection">The chosen participants, or all.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many were removed and kept; 403 for analysts and viewers; 404.</returns>
    [HttpPost("participants/remove")]
    [ProducesResponseType<RemovedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid surveyId, ParticipantSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        if (await RefusedAsync(survey.TenantId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var ids = selection.ParticipantIds ?? [];
        var chosen = context.Participants.Where(row => row.SurveyId == surveyId && (selection.All == true || ids.Contains(row.Id)));
        var kept = await chosen.CountAsync(row => context.Responses.Any(response => response.ParticipantId == row.Id), cancellationToken);
        var removed = await chosen.Where(row => !context.Responses.Any(response => response.ParticipantId == row.Id)).ExecuteDeleteAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), survey.TenantId, AuditActorKind.User, User.UserId().ToString(), "participants.removed", "Survey", survey.Id.ToString(), $"{removed} removed, {kept} kept", trace.CurrentTraceId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new RemovedResponse(removed, kept));
    }

    /// <summary>Every participant's personal link as CSV, to send another way (W12 D, email not set up).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>email, name, language, link; UTF-8 with a byte order mark.</returns>
    [HttpGet("participants/links.csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Links(Guid surveyId, CancellationToken cancellationToken)
    {
        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        var links = await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken);
        var invitations = await context.Invitations.Where(row => row.SurveyId == surveyId).ToDictionaryAsync(row => row.Id, row => row.Host, cancellationToken);
        var csv = new StringBuilder("email,name,language,link\r\n");

        foreach (var participant in await context.Participants.Where(row => row.SurveyId == surveyId).OrderBy(row => row.CreatedAt).ThenBy(row => row.Email).ToListAsync(cancellationToken))
        {
            csv.Append(string.Join(',', new[] { participant.Email, participant.Name ?? "", participant.Language ?? "", PersonalLink(links, invitations[participant.InvitationId], participant.Token) }.Select(Field))).Append("\r\n");
        }

        await audit.WriteAsync(new AuditContent(time.GetUtcNow(), survey.TenantId, AuditActorKind.User, User.UserId().ToString(), "participants.links-exported", "Survey", survey.Id.ToString(), Diff: null, trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "participant-links.csv");
    }

    /// <summary>A participant's link: the invitation's address, else the primary one, with their token.</summary>
    internal static string PersonalLink(IReadOnlyList<SurveyLinkResponse> links, string? host, string token) =>
        $"{(links.FirstOrDefault(link => string.Equals(link.Hostname, host, StringComparison.OrdinalIgnoreCase)) ?? links[0]).Url}?t={token}";

    private static string Field(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    /// <summary>
    /// The statuses in the order of an invitation's life, for sorting: waiting (to be sent) or link-only (made
    /// without email), sent, opened, started, answered; then bounced and unsubscribed, which outrank the rest.
    /// </summary>
    private static readonly string[] Statuses = ["waiting", "link-only", "sent", "opened", "started", "answered", "bounced", "unsubscribed"];

    /// <summary>Only the people a search, a status and a field choose (W35).</summary>
    private static IQueryable<ListedParticipant> Filtered(IQueryable<ListedParticipant> rows, string? search, string? status, string? field)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(row => EF.Functions.ILike(row.Participant.Email, pattern) || EF.Functions.ILike(row.Participant.Name ?? "", pattern) || EF.Functions.ILike(row.Participant.ExternalId ?? "", pattern));
        }

        if (status == "not-answered")
        {
            rows = rows.Where(row => row.Rank < 5);
        }
        else if (Array.IndexOf(Statuses, status) is var rank and >= 0)
        {
            rows = rows.Where(row => row.Rank == rank);
        }

        if (field?.Split(':', 2) is [{ Length: > 0 } key, var value])
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { [PersonDetails.FieldKey(key)] = value });
            rows = rows.Where(row => EF.Functions.JsonContains(row.Participant.Fields, json));
        }

        return rows;
    }

    /// <summary>The page's order; ties go by id, so pages never overlap.</summary>
    private static IQueryable<ListedParticipant> Sorted(IQueryable<ListedParticipant> rows, string? sort)
    {
        var descending = sort?.StartsWith('-') ?? true;

        IOrderedQueryable<ListedParticipant> By<TKey>(Expression<Func<ListedParticipant, TKey>> key) =>
            descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

        var ordered = sort?.TrimStart('-') switch
        {
            "name" => By(row => row.Participant.Name ?? row.Participant.Email),
            "status" => By(row => row.Rank),
            "sent" => By(row => row.Participant.SentAt),
            _ => By(row => row.Participant.CreatedAt),
        };

        return descending ? ordered.ThenByDescending(row => row.Participant.Id) : ordered.ThenBy(row => row.Participant.Id);
    }

    /// <summary>The people of a list who are new to this survey and have not unsubscribed.</summary>
    private Task<ParticipantCheckResponse> CheckAsync(Guid surveyId, ParsedList list, CancellationToken cancellationToken) =>
        CheckPeopleAsync(surveyId, [.. list.People.Select(person => new ParticipantInput(person.Email, person.Name, person.Language))], cancellationToken, list.Duplicates, [.. list.Invalid.Select(problem => new ListLine(problem.Line, problem.Text))]);

    /// <summary>
    /// The people who are new to this survey and have not unsubscribed (W35): the same External ID, else the same
    /// email, counts as already invited; a person twice in the list counts once.
    /// </summary>
    internal async Task<ParticipantCheckResponse> CheckPeopleAsync(Guid surveyId, IReadOnlyList<ParticipantInput> people, CancellationToken cancellationToken, int duplicates = 0, IReadOnlyList<ListLine>? invalid = null)
    {
        var lines = (invalid ?? []).ToList();
        var valid = new List<ParticipantInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (person, index) in people.Select((person, index) => (person, index)))
        {
            var email = (person.Email ?? "").Trim().ToLowerInvariant();

            if (!ParticipantList.IsAddress(email))
            {
                lines.Add(new ListLine(index + 1, person.Email ?? ""));
            }
            else if (!seen.Add(email) || (person.ExternalId is { Length: > 0 } id && !seen.Add("#" + id.Trim())))
            {
                duplicates++;
            }
            else
            {
                valid.Add(person with { Email = email });
            }
        }

        var emails = valid.Select(person => person.Email!).ToList();
        var ids = valid.Where(person => !string.IsNullOrWhiteSpace(person.ExternalId)).Select(person => person.ExternalId!.Trim()).ToList();
        var known = await context.Participants
            .Where(row => row.SurveyId == surveyId && (emails.Contains(row.Email) || (row.ExternalId != null && ids.Contains(row.ExternalId))))
            .Select(row => new { row.Email, row.ExternalId })
            .ToListAsync(cancellationToken);
        var knownEmails = known.Select(row => row.Email).ToHashSet(StringComparer.Ordinal);
        var knownIds = known.Where(row => row.ExternalId != null).Select(row => row.ExternalId!).ToHashSet(StringComparer.Ordinal);
        var unsubscribed = (await context.Unsubscribes.Where(row => emails.Contains(row.Email)).Select(row => row.Email).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        bool Invited(ParticipantInput person) => knownEmails.Contains(person.Email!) || (person.ExternalId is { Length: > 0 } id && knownIds.Contains(id.Trim()));

        return new ParticipantCheckResponse(
            [.. valid.Where(person => !Invited(person) && !unsubscribed.Contains(person.Email!))],
            duplicates,
            valid.Count(Invited),
            valid.Count(person => unsubscribed.Contains(person.Email!)),
            lines);
    }

    private Task<Survey?> SurveyAsync(Guid surveyId, CancellationToken cancellationToken) =>
        context.Surveys.FirstOrDefaultAsync(row => row.Id == surveyId && row.DeletedAt == null, cancellationToken);

    private ObjectResult Problem(int status, string code) =>
        StatusCode(status, new ParticipantProblem(code, System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    private async Task<IActionResult?> RefusedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Inviters.Contains(known) ? null : Problem(StatusCodes.Status403Forbidden, "role-not-allowed");
    }
}

/// <summary>Pasted lines.</summary>
/// <param name="Text">One person per line.</param>
public sealed record ParticipantCheckRequest(string? Text);

/// <summary>A person to invite.</summary>
/// <param name="Email">The address.</param>
/// <param name="Name">For {name}.</param>
/// <param name="Language">Their language, e.g. de.</param>
/// <param name="ExternalId">Their id in another system (W35); matches them before the email.</param>
/// <param name="Fields">Further fields (W35), e.g. department; placeholders in messages.</param>
public sealed record ParticipantInput(string? Email, string? Name, string? Language, string? ExternalId = null, IReadOnlyDictionary<string, string>? Fields = null)
{
    /// <summary>The cleaned-up details.</summary>
    public PersonDetails Details() => new(Email ?? "", Name, Language is { Length: 2 } ? Language : null, ExternalId, Fields);
}

/// <summary>A line that is not an address.</summary>
/// <param name="Line">1-based.</param>
/// <param name="Text">What it says.</param>
public sealed record ListLine(int Line, string Text);

/// <summary>Who a list would invite.</summary>
/// <param name="Participants">New people, each once.</param>
/// <param name="Duplicates">Addresses that came again in the list.</param>
/// <param name="AlreadyInvited">Addresses already invited to the survey.</param>
/// <param name="Unsubscribed">Addresses that asked for no more invitations.</param>
/// <param name="Invalid">Lines that are not addresses.</param>
public sealed record ParticipantCheckResponse(IReadOnlyList<ParticipantInput> Participants, int Duplicates, int AlreadyInvited, int Unsubscribed, IReadOnlyList<ListLine> Invalid);

/// <summary>An invitation to send.</summary>
/// <param name="Participants">The people, usually from the check.</param>
/// <param name="SendAt">When (UTC); now when empty or past.</param>
/// <param name="Deadline">"Answer by", for {deadline}; two weeks after sending when empty.</param>
/// <param name="Reminder">One reminder three days before the deadline to those who have not answered (default true).</param>
/// <param name="Send">Send emails (default true); false makes personal links only.</param>
/// <param name="Host">The address for the links; the primary one when empty.</param>
/// <param name="ContactIds">Contacts to invite as well (W35), with their fields.</param>
/// <param name="SaveToContacts">Also keep the people in Contacts (W35); known ones get the new details.</param>
/// <param name="ContactGroup">With SaveToContacts: the group they join.</param>
public sealed record InviteRequest(IReadOnlyList<ParticipantInput>? Participants, DateTimeOffset? SendAt = null, DateOnly? Deadline = null, bool? Reminder = null, bool? Send = null, string? Host = null, IReadOnlyList<Guid>? ContactIds = null, bool? SaveToContacts = null, string? ContactGroup = null);

/// <summary>What an invitation did.</summary>
/// <param name="Invited">People added.</param>
/// <param name="Skipped">Already invited, unsubscribed, duplicate or not an address.</param>
public sealed record InvitedResponse(int Invited, int Skipped);

/// <summary>A participant in the list.</summary>
/// <param name="Id">The participant.</param>
/// <param name="Email">The address.</param>
/// <param name="Name">The name, if given.</param>
/// <param name="Language">The language, if given.</param>
/// <param name="Status">waiting, link-only, sent, opened, started, answered, bounced or unsubscribed.</param>
/// <param name="SentAt">Invitation sent (UTC).</param>
/// <param name="Reminders">Reminders sent.</param>
/// <param name="Link">Their personal link.</param>
/// <param name="ExternalId">Their id in another system (W35).</param>
/// <param name="Fields">Their further fields (W35).</param>
/// <param name="LastActivity">The last thing that happened: answering, opening, a reminder, the invitation (W35).</param>
/// <param name="RemindedAt">The last reminder (UTC).</param>
/// <param name="RemindersOff">No more reminders for them (W35).</param>
/// <param name="ContactId">Their contact, if any (W35).</param>
public sealed record ParticipantItem(Guid Id, string Email, string? Name, string? Language, string Status, DateTimeOffset? SentAt, int Reminders, string Link, string? ExternalId = null, IReadOnlyDictionary<string, string>? Fields = null, DateTimeOffset? LastActivity = null, DateTimeOffset? RemindedAt = null, bool RemindersOff = false, Guid? ContactId = null);

/// <summary>One page of the participants of a survey.</summary>
/// <param name="Items">The page.</param>
/// <param name="Total">How many participants the survey has.</param>
/// <param name="Counts">How many per status.</param>
/// <param name="ReminderAt">The next planned reminder (UTC).</param>
/// <param name="Matching">How many the search and filters choose (W35); the pages count these.</param>
/// <param name="FieldKeys">The fields the survey's participants have (W35), to filter by.</param>
/// <param name="AutoReminders">The automatic reminders (W35) and the next one.</param>
public sealed record ParticipantListResponse(IReadOnlyList<ParticipantItem> Items, int Total, IReadOnlyDictionary<string, int> Counts, DateTimeOffset? ReminderAt, int Matching = 0, IReadOnlyList<string>? FieldKeys = null, AutoReminderSettings? AutoReminders = null);

/// <summary>A participant with what the list needs to show and sort it.</summary>
public sealed class ListedParticipant
{
    /// <summary>The participant.</summary>
    public required Participant Participant { get; init; }

    /// <summary>Their invitation's address.</summary>
    public string? Host { get; init; }

    /// <summary>Their status as an index into the statuses.</summary>
    public int Rank { get; init; }
}

/// <summary>Chosen participants (W13 C).</summary>
/// <param name="ParticipantIds">The chosen ones.</param>
/// <param name="All">Every participant of the survey instead.</param>
public sealed record ParticipantSelection(IReadOnlyList<Guid>? ParticipantIds, bool? All = null);

/// <summary>What a removal did.</summary>
/// <param name="Removed">Participants removed.</param>
/// <param name="Kept">Chosen but kept, because they started or sent a response.</param>
public sealed record RemovedResponse(int Removed, int Kept);

/// <summary>Why an invitation was refused.</summary>
/// <param name="Code">role-not-allowed, not-published or email-not-set-up.</param>
/// <param name="TraceId">The request's trace.</param>
public sealed record ParticipantProblem(string Code, string? TraceId);

/// <summary>Payload of a send-invitations job.</summary>
/// <param name="InvitationId">The invitation.</param>
/// <param name="ParticipantIds">Again to these people only (W35), whichever invitation they came with.</param>
public sealed record SendInvitationsPayload(Guid InvitationId, IReadOnlyList<Guid>? ParticipantIds = null);

/// <summary>Payload of a send-reminders job.</summary>
/// <param name="SurveyId">The survey.</param>
/// <param name="InvitationId">Only this invitation's people; everyone invited when empty.</param>
/// <param name="ParticipantIds">Only these people, when given.</param>
public sealed record SendRemindersPayload(Guid SurveyId, Guid? InvitationId, IReadOnlyList<Guid>? ParticipantIds = null);
