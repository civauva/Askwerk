// SPDX-License-Identifier: AGPL-3.0-only
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
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Participants, rich (W35): a list read as a table to map its columns, the mapped people checked, one person with
/// their history to change, reminders off, the invitation again, chosen people into Contacts, everything as CSV, and
/// the automatic reminders. Owners, admins and editors change; everyone in the organisation reads.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/surveys/{surveyId:guid}")]
public sealed class ParticipantPeopleController(
    AskwerkDbContext context,
    IInstanceSettingsRepository settings,
    EmailSettingsProvider email,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ITraceContext trace) : ControllerBase
{
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private static readonly TenantRole[] Editors = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];
    private static readonly string[] ExportColumns = ["email", "name", "language", "external_id", "status", "sent_at", "last_activity", "reminders", "link"];

    /// <summary>A file read as a table (W35 import step 1): its columns, a guess for each, and every row.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="file">CSV or Excel, at most 5 MB.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The table; 400 when it is too large or empty.</returns>
    [HttpPost("participants/table")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PeopleTable>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PeopleTable>> TableFromFile(Guid surveyId, IFormFile file, CancellationToken cancellationToken)
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

        return PeopleTables.Build(this, ParticipantList.ReadTable(file.FileName, buffer.ToArray()), file.FileName);
    }

    /// <summary>A pasted table (W35): cells split by tabs as Excel copies them, else CSV; the header first.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">The pasted text.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The table; 400 when it is empty.</returns>
    [HttpPost("participants/table")]
    [Consumes("application/json")]
    [ProducesResponseType<PeopleTable>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PeopleTable>> TableFromText(Guid surveyId, ParticipantCheckRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await SurveyAsync(surveyId, cancellationToken) is null ? NotFound() : PeopleTables.Build(this, ParticipantList.ReadPasted(request.Text), null);
    }

    /// <summary>Checks mapped people (W35 import step 3): who is new, already invited (External ID or email), unsubscribed, a duplicate or no address.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">The people as the mapping made them.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The check.</returns>
    [HttpPost("participants/check-people")]
    [ProducesResponseType<ParticipantCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParticipantCheckResponse>> CheckPeople(Guid surveyId, CheckPeopleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await SurveyAsync(surveyId, cancellationToken) is null)
        {
            return NotFound();
        }

        var participants = new ParticipantsController(context, settings, email, audit, unitOfWork, time, trace);

        return await participants.CheckPeopleAsync(surveyId, request.People ?? [], cancellationToken);
    }

    /// <summary>One person (W35): their details, personal link and history.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="participantId">The participant.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The person; 404.</returns>
    [HttpGet("participants/{participantId:guid}")]
    [ProducesResponseType<ParticipantDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParticipantDetail>> Detail(Guid surveyId, Guid participantId, CancellationToken cancellationToken)
    {
        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey
            || await context.Participants.FirstOrDefaultAsync(row => row.Id == participantId && row.SurveyId == surveyId, cancellationToken) is not { } person)
        {
            return NotFound();
        }

        var invitation = await context.Invitations.SingleAsync(row => row.Id == person.InvitationId, cancellationToken);
        var responses = await context.Responses.Where(row => row.ParticipantId == person.Id).OrderBy(row => row.StartedAt).ToListAsync(cancellationToken);
        var unsubscribedAt = await context.Unsubscribes.Where(row => row.Email == person.Email).Select(row => (DateTimeOffset?)row.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var links = await SurveyLinks.OfAsync(context, settings, Request, survey, cancellationToken);
        var history = new List<ParticipantEvent> { new("invited", person.CreatedAt, null) };
        history.AddRange(person.SentAt is { } sent ? [new ParticipantEvent("sent", sent, null)] : []);
        history.AddRange(person.RemindedAt is { } reminded ? [new ParticipantEvent("reminded", reminded, person.Reminders)] : []);
        history.AddRange(person.OpenedAt is { } opened ? [new ParticipantEvent("opened", opened, null)] : []);
        history.AddRange(responses.Select(response => new ParticipantEvent("started", response.StartedAt, null)));
        history.AddRange(responses.Where(response => response.CompletedAt is not null).Select(response => new ParticipantEvent("answered", response.CompletedAt!.Value, null)));
        history.AddRange(person.BouncedAt is { } bounced ? [new ParticipantEvent("bounced", bounced, null)] : []);
        history.AddRange(unsubscribedAt is { } unsubscribed ? [new ParticipantEvent("unsubscribed", unsubscribed, null)] : []);
        var auto = await AutoReminders.SettingsAsync(context, survey, time.GetUtcNow(), cancellationToken);
        var next = survey.AutoReminders && !person.RemindersOff && person.Reminders < survey.ReminderMax && person.SentAt is not null && responses.All(response => response.CompletedAt is null) && person.BouncedAt is null && unsubscribedAt is null
            ? AutoReminders.NextRun((person.RemindedAt ?? person.SentAt!.Value).AddDays(survey.ReminderDays), time.GetUtcNow())
            : (DateTimeOffset?)null;

        return new ParticipantDetail(
            person.Id,
            person.Email,
            person.Name,
            person.Language,
            person.ExternalId,
            PersonDetails.ReadFields(person.Fields),
            ParticipantsController.PersonalLink(links, invitation.Host, person.Token),
            person.RemindersOff,
            person.ContactId,
            [.. history.OrderBy(entry => entry.At)],
            next,
            auto.On);
    }

    /// <summary>Changes a person (W35): email, name, language, External ID and fields; their link stays the same.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="participantId">The participant.</param>
    /// <param name="request">The new details.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at the field (email-invalid, email-taken, external-id-taken); 403; 404.</returns>
    [HttpPut("participants/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Change(Guid surveyId, Guid participantId, ParticipantInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await PersonAsync(surveyId, participantId, cancellationToken) is not ({ } survey, { } person))
        {
            return NotFound();
        }

        if (await RefusedAsync(survey.TenantId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var details = request.Details();

        if (!ParticipantList.IsAddress(details.Email))
        {
            ModelState.AddModelError("email", "email-invalid");
        }
        else if (await context.Participants.AnyAsync(row => row.SurveyId == surveyId && row.Id != person.Id && row.Email == details.Email, cancellationToken))
        {
            ModelState.AddModelError("email", "email-taken");
        }

        if (details.ExternalId is { } externalId && await context.Participants.AnyAsync(row => row.SurveyId == surveyId && row.Id != person.Id && row.ExternalId == externalId, cancellationToken))
        {
            ModelState.AddModelError("externalId", "external-id-taken");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        person.Change(details);
        await AuditAsync(survey, "participant.changed", person.Id.ToString(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>No more reminders for a person, or reminders again (W35).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="participantId">The participant.</param>
    /// <param name="request">Off or on.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403; 404.</returns>
    [HttpPut("participants/{participantId:guid}/reminders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemindersOff(Guid surveyId, Guid participantId, RemindersOffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await PersonAsync(surveyId, participantId, cancellationToken) is not ({ } survey, { } person))
        {
            return NotFound();
        }

        if (await RefusedAsync(survey.TenantId, cancellationToken) is { } refused)
        {
            return refused;
        }

        person.SetRemindersOff(request.Off);
        await AuditAsync(survey, request.Off ? "participant.reminders-off" : "participant.reminders-on", person.Id.ToString(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>The invitation again to the chosen people (W35), sent before or not.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="selection">The chosen participants.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>202; 403; 404; 409 email-not-set-up.</returns>
    [HttpPost("participants/resend")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resend(Guid surveyId, ParticipantSelection selection, CancellationToken cancellationToken)
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

        if (await email.FindAsync(cancellationToken) is null)
        {
            return Problem(StatusCodes.Status409Conflict, "email-not-set-up");
        }

        var ids = await Chosen(surveyId, selection).Select(row => row.Id).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();

        if (ids.Count > 0)
        {
            JobRunner.Enqueue(context, survey.TenantId, ParticipantsController.SendInvitations, new SendInvitationsPayload(Guid.Empty, ids), now, now);
        }

        await AuditAsync(survey, "participants.invited-again", $"{ids.Count} chosen", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status202Accepted);
    }

    /// <summary>Puts the chosen people into Contacts, in a group (W35); known contacts get their new details.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">The chosen participants and the group.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many contacts were added and updated; 403; 404.</returns>
    [HttpPost("participants/contacts")]
    [ProducesResponseType<ContactsSavedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveToContacts(Guid surveyId, SaveToContactsRequest request, CancellationToken cancellationToken)
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

        var people = await Chosen(surveyId, new ParticipantSelection(request.ParticipantIds, request.All)).ToListAsync(cancellationToken);
        var inputs = people.Select(person => new ParticipantInput(person.Email, person.Name, person.Language, person.ExternalId, PersonDetails.ReadFields(person.Fields))).ToList();
        var book = await ContactBook.MatchAsync(context, inputs, cancellationToken);
        var now = time.GetUtcNow();
        var (added, updated) = (0, 0);
        string[] groups = string.IsNullOrWhiteSpace(request.Group) ? [] : [request.Group.Trim()];

        foreach (var person in people)
        {
            var details = new PersonDetails(person.Email, person.Name, person.Language, person.ExternalId, PersonDetails.ReadFields(person.Fields));

            if (book.Find(details) is { } contact)
            {
                contact.Merge(details, now);
                contact.Join(groups, now);
                person.LinkContact(contact.Id);
                updated++;
            }
            else
            {
                var created = Contact.Create(Guid.CreateVersion7(now), survey.TenantId, details, groups, now);
                context.Contacts.Add(created);
                book.Add(created);
                person.LinkContact(created.Id);
                added++;
            }
        }

        await AuditAsync(survey, "participants.saved-to-contacts", $"{added} added, {updated} updated", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new ContactsSavedResponse(added, updated));
    }

    /// <summary>
    /// Every participant as CSV (W35): email, name, language, External ID, status, dates, reminders, link and each field;
    /// the same search and filters as the list. UTF-8 with a byte order mark, for Excel.
    /// </summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="search">As in the list.</param>
    /// <param name="status">As in the list.</param>
    /// <param name="field">As in the list.</param>
    /// <returns>The file; 404.</returns>
    [HttpGet("participants.csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Export(Guid surveyId, CancellationToken cancellationToken, string? search = null, string? status = null, string? field = null) =>
        ExportAsync(surveyId, null, search, status, field, cancellationToken);

    /// <summary>The chosen participants as CSV (W35).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="selection">The chosen participants.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file; 404.</returns>
    [HttpPost("participants.csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ExportChosen(Guid surveyId, ParticipantSelection selection, CancellationToken cancellationToken) =>
        ExportAsync(surveyId, selection, null, null, null, cancellationToken);

    /// <summary>The automatic reminders (W35): on or off, after how many days (1 to 60), at most how many (1 to 5).</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">The setting.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The setting with the next reminder; 403; 404.</returns>
    [HttpPut("reminders")]
    [ProducesResponseType<AutoReminderSettings>(StatusCodes.Status200OK)]
    [ProducesResponseType<ParticipantProblem>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetReminders(Guid surveyId, AutoRemindersRequest request, CancellationToken cancellationToken)
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

        var now = time.GetUtcNow();
        survey.SetAutoReminders(request.On, request.Days ?? survey.ReminderDays, request.Max ?? survey.ReminderMax, now);
        AutoReminders.Plan(context, survey, now);
        await AuditAsync(survey, "survey.reminders-changed", $"{(request.On ? "on" : "off")}, {survey.ReminderDays} days, at most {survey.ReminderMax}", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(await AutoReminders.SettingsAsync(context, survey, now, cancellationToken));
    }

    private async Task<IActionResult> ExportAsync(Guid surveyId, ParticipantSelection? selection, string? search, string? status, string? field, CancellationToken cancellationToken)
    {
        if (await SurveyAsync(surveyId, cancellationToken) is not { } survey)
        {
            return NotFound();
        }

        // The list's own query, so the export shows what the list shows.
        var participants = new ParticipantsController(context, settings, email, audit, unitOfWork, time, trace) { ControllerContext = ControllerContext };
        var items = new List<ParticipantItem>();

        for (var offset = 0; ; offset += 100)
        {
            var page = (await participants.List(surveyId, "name", offset, 100, cancellationToken, search, status, field)).Value!;
            var chosen = selection is { All: not true, ParticipantIds: { } ids } ? page.Items.Where(item => ids.Contains(item.Id)) : page.Items;
            items.AddRange(chosen);

            if (page.Items.Count < 100)
            {
                break;
            }
        }

        var keys = items.SelectMany(item => item.Fields?.Keys ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var csv = new StringBuilder();
        csv.Append(string.Join(',', ExportColumns.Concat(keys).Select(Field))).Append("\r\n");

        foreach (var item in items)
        {
            string Date(DateTimeOffset? at) => at?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";
            csv.Append(string.Join(',', new[] { item.Email, item.Name ?? "", item.Language ?? "", item.ExternalId ?? "", item.Status, Date(item.SentAt), Date(item.LastActivity), item.Reminders.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Link }
                .Concat(keys.Select(key => item.Fields?.GetValueOrDefault(key) ?? "")).Select(Field))).Append("\r\n");
        }

        await AuditAsync(survey, "participants.exported", $"{items.Count} people", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "participants.csv");
    }

    private static string Field(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n;") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    private IQueryable<Participant> Chosen(Guid surveyId, ParticipantSelection selection)
    {
        var ids = selection.ParticipantIds ?? [];

        return context.Participants.Where(row => row.SurveyId == surveyId && (selection.All == true || ids.Contains(row.Id)));
    }

    private async Task<(Survey?, Participant?)> PersonAsync(Guid surveyId, Guid participantId, CancellationToken cancellationToken) =>
        await SurveyAsync(surveyId, cancellationToken) is { } survey
            ? (survey, await context.Participants.FirstOrDefaultAsync(row => row.Id == participantId && row.SurveyId == surveyId, cancellationToken))
            : (null, null);

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

        return role is { } known && Editors.Contains(known) ? null : Problem(StatusCodes.Status403Forbidden, "role-not-allowed");
    }

    private Task AuditAsync(Survey survey, string action, string? detail, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditContent(time.GetUtcNow(), survey.TenantId, AuditActorKind.User, User.UserId().ToString(), action, "Survey", survey.Id.ToString(), detail, trace.CurrentTraceId), cancellationToken);
}

/// <summary>Lists read as tables (W35), for participants and contacts alike.</summary>
internal static class PeopleTables
{
    private const int MaxRows = 20_000;

    /// <summary>The table with a guess per column; a guess is used once, a second email or name column is a field.</summary>
    public static ActionResult<PeopleTable> Build(ControllerBase controller, IReadOnlyList<IReadOnlyList<string>> rows, string? fileName)
    {
        if (rows.Count < 2)
        {
            return controller.ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["no-rows"] }));
        }

        if (rows.Count > MaxRows + 1)
        {
            return controller.ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["too-many-rows"] }));
        }

        var header = rows[0];
        var width = rows.Max(row => row.Count);
        var columns = Enumerable.Range(0, width).Select(index => index < header.Count && header[index].Length > 0 ? header[index] : $"Column {index + 1}").ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var guesses = columns.Select(ParticipantList.Guess).Select(guess => guess == "field" || used.Add(guess) ? guess : "field").ToList();

        return new PeopleTable(fileName, columns, guesses, [.. rows.Skip(1).Select(row => (IReadOnlyList<string>)[.. Enumerable.Range(0, width).Select(index => index < row.Count ? row[index] : "")])]);
    }
}

/// <summary>A list read as a table (W35).</summary>
/// <param name="FileName">The file's name, if it was a file.</param>
/// <param name="Columns">The header of each column.</param>
/// <param name="Guesses">What each column most likely holds: email, name, firstName, lastName, language, externalId or field.</param>
/// <param name="Rows">Every row after the header, as wide as the table.</param>
public sealed record PeopleTable(string? FileName, IReadOnlyList<string> Columns, IReadOnlyList<string> Guesses, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>People to check.</summary>
/// <param name="People">As the column mapping made them.</param>
public sealed record CheckPeopleRequest(IReadOnlyList<ParticipantInput>? People);

/// <summary>One thing that happened to a person (W35).</summary>
/// <param name="Kind">invited, sent, reminded, opened, started, answered, bounced or unsubscribed.</param>
/// <param name="At">When (UTC).</param>
/// <param name="Count">Reminded: how many reminders so far.</param>
public sealed record ParticipantEvent(string Kind, DateTimeOffset At, int? Count);

/// <summary>One person (W35).</summary>
/// <param name="Id">The participant.</param>
/// <param name="Email">The address.</param>
/// <param name="Name">The name.</param>
/// <param name="Language">The language.</param>
/// <param name="ExternalId">Their id in another system.</param>
/// <param name="Fields">Further fields.</param>
/// <param name="Link">The personal link.</param>
/// <param name="RemindersOff">No more reminders.</param>
/// <param name="ContactId">Their contact, if any.</param>
/// <param name="History">What happened, oldest first.</param>
/// <param name="NextReminderAt">The next automatic reminder for them (UTC), if any.</param>
/// <param name="AutoReminders">Whether the survey reminds by itself.</param>
public sealed record ParticipantDetail(Guid Id, string Email, string? Name, string? Language, string? ExternalId, IReadOnlyDictionary<string, string> Fields, string Link, bool RemindersOff, Guid? ContactId, IReadOnlyList<ParticipantEvent> History, DateTimeOffset? NextReminderAt, bool AutoReminders);

/// <summary>Reminders off or on again.</summary>
/// <param name="Off">True for no more reminders.</param>
public sealed record RemindersOffRequest(bool Off);

/// <summary>Chosen participants into Contacts.</summary>
/// <param name="ParticipantIds">The chosen ones.</param>
/// <param name="All">Every participant instead.</param>
/// <param name="Group">The group they join, if any.</param>
public sealed record SaveToContactsRequest(IReadOnlyList<Guid>? ParticipantIds, bool? All = null, string? Group = null);

/// <summary>What saving to Contacts did.</summary>
/// <param name="Added">New contacts.</param>
/// <param name="Updated">Known contacts with new details.</param>
public sealed record ContactsSavedResponse(int Added, int Updated);

/// <summary>The automatic reminders to set.</summary>
/// <param name="On">On or off.</param>
/// <param name="Days">After how many days; unchanged when empty.</param>
/// <param name="Max">At most how many; unchanged when empty.</param>
public sealed record AutoRemindersRequest(bool On, int? Days = null, int? Max = null);
