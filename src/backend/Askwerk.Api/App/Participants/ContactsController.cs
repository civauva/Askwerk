// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Participants;
using Askwerk.Application.Persistence;
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
/// Contacts (W35): the organisation's address book. People with their External ID, fields and groups, invited to many
/// surveys; who unsubscribed or bounced cannot be invited. Owners, admins and editors change it; everyone reads it.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/contacts")]
public sealed class ContactsController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;
    private static readonly TenantRole[] Editors = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];
    private static readonly string[] ExportColumns = ["email", "name", "language", "external_id", "groups"];

    /// <summary>The contacts a page at a time, by name, with their groups, how many surveys they were invited to and answered, and the group and state counts.</summary>
    /// <param name="search">Part of the name, email or External ID.</param>
    /// <param name="group">Only this group.</param>
    /// <param name="state">invitable, unsubscribed or bounced.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="limit">Rows per page, at most 100.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page.</returns>
    [HttpGet]
    [ProducesResponseType<ContactListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContactListResponse>> List(string? search, string? group, string? state, int offset, int? limit, CancellationToken cancellationToken)
    {
        var all = context.Contacts.AsQueryable();
        var rows = all.Select(contact => new
        {
            contact,
            Unsubscribed = context.Unsubscribes.Any(row => row.Email == contact.Email),
            UnsubscribedAt = context.Unsubscribes.Where(row => row.Email == contact.Email).Min(row => (DateTimeOffset?)row.CreatedAt),
            Bounced = context.Participants.Any(row => row.BouncedAt != null && (row.ContactId == contact.Id || row.Email == contact.Email)),
        });
        var counts = new Dictionary<string, int>
        {
            ["all"] = await all.CountAsync(cancellationToken),
            ["unsubscribed"] = await rows.CountAsync(row => row.Unsubscribed, cancellationToken),
            ["bounced"] = await rows.CountAsync(row => !row.Unsubscribed && row.Bounced, cancellationToken),
        };
        counts["invitable"] = counts["all"] - counts["unsubscribed"] - counts["bounced"];
        var groups = await context.Database
            .SqlQuery<GroupCount>($"SELECT g AS \"Name\", count(*)::int AS \"Count\" FROM contact CROSS JOIN LATERAL unnest(groups) AS g GROUP BY g ORDER BY g")
            .ToListAsync(cancellationToken);
        var fieldKeys = await context.Database.SqlQuery<string>($"SELECT DISTINCT jsonb_object_keys(fields) AS \"Value\" FROM contact ORDER BY 1").ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(row => EF.Functions.ILike(row.contact.Email, pattern) || EF.Functions.ILike(row.contact.Name ?? "", pattern) || EF.Functions.ILike(row.contact.ExternalId ?? "", pattern));
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            rows = rows.Where(row => row.contact.Groups.Contains(group));
        }

        rows = state switch
        {
            "unsubscribed" => rows.Where(row => row.Unsubscribed),
            "bounced" => rows.Where(row => !row.Unsubscribed && row.Bounced),
            "invitable" => rows.Where(row => !row.Unsubscribed && !row.Bounced),
            _ => rows,
        };

        var matching = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(row => row.contact.Name ?? row.contact.Email).ThenBy(row => row.contact.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit))
            .Select(row => new
            {
                row.contact,
                row.Unsubscribed,
                row.UnsubscribedAt,
                row.Bounced,
                Surveys = context.Participants.Count(participant => participant.ContactId == row.contact.Id),
                Answered = context.Participants.Count(participant => participant.ContactId == row.contact.Id && context.Responses.Any(response => response.ParticipantId == participant.Id && response.Status == ResponseStatus.Complete)),
                LastInvited = context.Participants.Where(participant => participant.ContactId == row.contact.Id).Max(participant => (DateTimeOffset?)participant.CreatedAt),
            })
            .ToListAsync(cancellationToken);

        return new ContactListResponse(
            [.. page.Select(row => new ContactItem(row.contact.Id, row.contact.Email, row.contact.Name, row.contact.Language, row.contact.ExternalId, PersonDetails.ReadFields(row.contact.Fields), row.contact.Groups, row.Surveys, row.Answered, row.LastInvited, !row.Unsubscribed && !row.Bounced, row.Unsubscribed, row.Bounced, row.UnsubscribedAt))],
            matching,
            counts,
            [.. groups.Select(entry => new ContactGroup(entry.Name, entry.Count))],
            fieldKeys);
    }

    /// <summary>Adds a contact.</summary>
    /// <param name="request">The person and their groups.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the id; 400 at the field (email-invalid, email-taken, external-id-taken); 403.</returns>
    [HttpPost]
    [ProducesResponseType<ContactCreated>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(ContactInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        var details = request.Details();

        if (await ProblemsAsync(details, null, cancellationToken) is { } problems)
        {
            return problems;
        }

        var now = time.GetUtcNow();
        var contact = Contact.Create(Guid.CreateVersion7(now), tenantId, details, request.Groups, now);
        context.Contacts.Add(contact);
        await AuditAsync(tenantId, "contact.added", contact.Id.ToString(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/contacts/{contact.Id}", new ContactCreated(contact.Id));
    }

    /// <summary>Changes a contact: details and groups.</summary>
    /// <param name="id">The contact.</param>
    /// <param name="request">The new details and groups.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 at the field; 403; 404.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Change(Guid id, ContactInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        if (await context.Contacts.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) is not { } contact)
        {
            return NotFound();
        }

        var details = request.Details();

        if (await ProblemsAsync(details, id, cancellationToken) is { } problems)
        {
            return problems;
        }

        var now = time.GetUtcNow();
        contact.Change(details, now);

        if (request.Groups is { } groups)
        {
            contact.SetGroups(groups, now);
        }

        await AuditAsync(tenantId, "contact.changed", contact.Id.ToString(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Removes the chosen contacts; their participants in surveys stay, without the link to them.</summary>
    /// <param name="request">The chosen contacts.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many were removed; 403.</returns>
    [HttpPost("remove")]
    [ProducesResponseType<ContactsChanged>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Remove(ContactSelection request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        var ids = request.ContactIds ?? [];
        await context.Participants.Where(row => row.ContactId != null && ids.Contains(row.ContactId.Value)).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ContactId, (Guid?)null), cancellationToken);
        var removed = await context.Contacts.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);
        await AuditAsync(tenantId, "contacts.removed", $"{removed} removed", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new ContactsChanged(removed, 0));
    }

    /// <summary>Puts the chosen contacts into a group, or takes them out of it.</summary>
    /// <param name="request">The chosen contacts, the group, and whether to take them out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many changed; 400 group-required; 403.</returns>
    [HttpPost("groups")]
    [ProducesResponseType<ContactsChanged>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Group(ContactGroupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Group))
        {
            ModelState.AddModelError("group", "group-required");
            return ValidationProblem(ModelState);
        }

        var ids = request.ContactIds ?? [];
        var now = time.GetUtcNow();
        var contacts = await context.Contacts.Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            if (request.Leave == true)
            {
                contact.SetGroups(contact.Groups.Where(group => group != request.Group.Trim()), now);
            }
            else
            {
                contact.Join([request.Group], now);
            }
        }

        await AuditAsync(tenantId, request.Leave == true ? "contacts.left-group" : "contacts.joined-group", $"{contacts.Count} · {request.Group.Trim()}", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new ContactsChanged(0, contacts.Count));
    }

    /// <summary>A file read as a table (W35 import step 1): its columns, a guess for each, and every row.</summary>
    /// <param name="file">CSV or Excel, at most 5 MB.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The table; 400 when it is too large or empty.</returns>
    [HttpPost("table")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PeopleTable>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PeopleTable>> TableFromFile(IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length > 5 * 1024 * 1024)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["file-too-large"] }));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        return PeopleTables.Build(this, ParticipantList.ReadTable(file.FileName, buffer.ToArray()), file.FileName);
    }

    /// <summary>A pasted table (W35): cells split by tabs as Excel copies them, else CSV; the header first.</summary>
    /// <param name="request">The pasted text.</param>
    /// <returns>The table; 400 when it is empty.</returns>
    [HttpPost("table")]
    [Consumes("application/json")]
    [ProducesResponseType<PeopleTable>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<PeopleTable> TableFromText(ParticipantCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PeopleTables.Build(this, ParticipantList.ReadPasted(request.Text), null);
    }

    /// <summary>Imports people into Contacts (W35): by External ID, then email; known ones get the new details, new ones are added; all join the group.</summary>
    /// <param name="request">The people as the column mapping made them, and the group.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many were added and updated, and the lines that are no address; 403.</returns>
    [HttpPost("import")]
    [ProducesResponseType<ContactsImported>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Import(ContactImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await TenantAsync(cancellationToken) is not { } tenantId)
        {
            return Forbid();
        }

        var people = request.People ?? [];
        var book = await ContactBook.MatchAsync(context, people, cancellationToken);
        var now = time.GetUtcNow();
        var (added, updated) = (0, 0);
        var invalid = new List<ListLine>();
        string[] groups = string.IsNullOrWhiteSpace(request.Group) ? [] : [request.Group.Trim()];

        foreach (var (person, index) in people.Select((person, index) => (person, index)))
        {
            var details = person.Details();

            if (!ParticipantList.IsAddress(details.Email))
            {
                invalid.Add(new ListLine(index + 1, person.Email ?? ""));
            }
            else if (book.Find(details) is { } contact)
            {
                contact.Merge(details, now);
                contact.Join(groups, now);
                updated++;
            }
            else
            {
                var created = Contact.Create(Guid.CreateVersion7(now), tenantId, details, groups, now);
                context.Contacts.Add(created);
                book.Add(created);
                added++;
            }
        }

        await AuditAsync(tenantId, "contacts.imported", $"{added} added, {updated} updated", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new ContactsImported(added, updated, invalid));
    }

    /// <summary>The contacts as CSV: email, name, language, External ID, groups and each field; one group or all. UTF-8 with a byte order mark.</summary>
    /// <param name="group">Only this group.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file.</returns>
    [HttpGet("contacts.csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Export(string? group, CancellationToken cancellationToken) =>
        ExportAsync(context.Contacts.Where(row => group == null || row.Groups.Contains(group)), cancellationToken);

    /// <summary>The chosen contacts as CSV (W35), as the export of all.</summary>
    /// <param name="selection">The chosen contacts.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file.</returns>
    [HttpPost("contacts.csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> ExportChosen(ContactSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var ids = selection.ContactIds ?? [];

        return ExportAsync(context.Contacts.Where(row => ids.Contains(row.Id)), cancellationToken);
    }

    private async Task<IActionResult> ExportAsync(IQueryable<Contact> chosen, CancellationToken cancellationToken)
    {
        var contacts = await chosen.OrderBy(row => row.Name ?? row.Email).ToListAsync(cancellationToken);
        var fields = contacts.Select(contact => PersonDetails.ReadFields(contact.Fields)).ToList();
        var keys = fields.SelectMany(each => each.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var csv = new StringBuilder(string.Join(',', ExportColumns.Concat(keys).Select(Field))).Append("\r\n");

        foreach (var (contact, own) in contacts.Zip(fields))
        {
            csv.Append(string.Join(',', new[] { contact.Email, contact.Name ?? "", contact.Language ?? "", contact.ExternalId ?? "", string.Join("; ", contact.Groups) }
                .Concat(keys.Select(key => own.GetValueOrDefault(key) ?? "")).Select(Field))).Append("\r\n");
        }

        if (User.TenantId() is { } tenantId)
        {
            await AuditAsync(tenantId, "contacts.exported", $"{contacts.Count} contacts", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "contacts.csv");
    }

    private static string Field(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n;") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    private async Task<IActionResult?> ProblemsAsync(PersonDetails details, Guid? self, CancellationToken cancellationToken)
    {
        if (!ParticipantList.IsAddress(details.Email))
        {
            ModelState.AddModelError("email", "email-invalid");
        }
        else if (await context.Contacts.AnyAsync(row => row.Email == details.Email && row.Id != self, cancellationToken))
        {
            ModelState.AddModelError("email", "email-taken");
        }

        if (details.ExternalId is { } externalId && await context.Contacts.AnyAsync(row => row.ExternalId == externalId && row.Id != self, cancellationToken))
        {
            ModelState.AddModelError("externalId", "external-id-taken");
        }

        return ModelState.IsValid ? null : ValidationProblem(ModelState);
    }

    /// <summary>The organisation, when the user may change its contacts.</summary>
    private async Task<Guid?> TenantAsync(CancellationToken cancellationToken)
    {
        if (User.TenantId() is not { } tenantId)
        {
            return null;
        }

        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Editors.Contains(known) ? tenantId : null;
    }

    private Task AuditAsync(Guid tenantId, string action, string? detail, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditContent(time.GetUtcNow(), tenantId, AuditActorKind.User, User.UserId().ToString(), action, "Contact", detail ?? "", Diff: null, trace.CurrentTraceId), cancellationToken);

    private sealed class GroupCount
    {
        public string Name { get; set; } = "";

        public int Count { get; set; }
    }
}

/// <summary>A contact to add or change.</summary>
/// <param name="Email">The address.</param>
/// <param name="Name">The name.</param>
/// <param name="Language">E.g. de.</param>
/// <param name="ExternalId">Their id in another system.</param>
/// <param name="Fields">Further fields.</param>
/// <param name="Groups">Their groups; unchanged when empty on a change.</param>
public sealed record ContactInput(string? Email, string? Name, string? Language, string? ExternalId = null, IReadOnlyDictionary<string, string>? Fields = null, IReadOnlyList<string>? Groups = null)
{
    /// <summary>The cleaned-up details.</summary>
    public PersonDetails Details() => new(Email ?? "", Name, Language is { Length: 2 } ? Language : null, ExternalId, Fields);
}

/// <summary>One contact in the list.</summary>
/// <param name="Id">The contact.</param>
/// <param name="Email">The address.</param>
/// <param name="Name">The name.</param>
/// <param name="Language">The language.</param>
/// <param name="ExternalId">Their id in another system.</param>
/// <param name="Fields">Further fields.</param>
/// <param name="Groups">Their groups.</param>
/// <param name="Surveys">Surveys they were invited to.</param>
/// <param name="Answered">Surveys they answered.</param>
/// <param name="LastInvitedAt">The last invitation (UTC).</param>
/// <param name="Invitable">Neither unsubscribed nor bounced.</param>
/// <param name="Unsubscribed">They asked for no more invitations.</param>
/// <param name="Bounced">Their address was refused.</param>
/// <param name="UnsubscribedAt">When they unsubscribed (UTC).</param>
public sealed record ContactItem(Guid Id, string Email, string? Name, string? Language, string? ExternalId, IReadOnlyDictionary<string, string> Fields, IReadOnlyList<string> Groups, int Surveys, int Answered, DateTimeOffset? LastInvitedAt, bool Invitable, bool Unsubscribed, bool Bounced, DateTimeOffset? UnsubscribedAt = null);

/// <summary>A group and its size.</summary>
/// <param name="Name">The group.</param>
/// <param name="Count">Contacts in it.</param>
public sealed record ContactGroup(string Name, int Count);

/// <summary>One page of contacts.</summary>
/// <param name="Items">The page.</param>
/// <param name="Matching">How many the search and filters choose.</param>
/// <param name="Counts">all, invitable, unsubscribed, bounced.</param>
/// <param name="Groups">Every group with its size.</param>
/// <param name="FieldKeys">The fields contacts have.</param>
public sealed record ContactListResponse(IReadOnlyList<ContactItem> Items, int Matching, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<ContactGroup> Groups, IReadOnlyList<string> FieldKeys);

/// <summary>A new contact.</summary>
/// <param name="Id">Its id.</param>
public sealed record ContactCreated(Guid Id);

/// <summary>Chosen contacts.</summary>
/// <param name="ContactIds">The chosen ones.</param>
public sealed record ContactSelection(IReadOnlyList<Guid>? ContactIds);

/// <summary>Contacts into or out of a group.</summary>
/// <param name="ContactIds">The chosen ones.</param>
/// <param name="Group">The group.</param>
/// <param name="Leave">Take them out instead.</param>
public sealed record ContactGroupRequest(IReadOnlyList<Guid>? ContactIds, string? Group, bool? Leave = null);

/// <summary>What a change to many contacts did.</summary>
/// <param name="Removed">Contacts removed.</param>
/// <param name="Changed">Contacts changed.</param>
public sealed record ContactsChanged(int Removed, int Changed);

/// <summary>People to import into Contacts.</summary>
/// <param name="People">As the column mapping made them.</param>
/// <param name="Group">The group they join, if any.</param>
public sealed record ContactImportRequest(IReadOnlyList<ParticipantInput>? People, string? Group = null);

/// <summary>What an import into Contacts did.</summary>
/// <param name="Added">New contacts.</param>
/// <param name="Updated">Known contacts with new details.</param>
/// <param name="Invalid">Lines that are no address.</param>
public sealed record ContactsImported(int Added, int Updated, IReadOnlyList<ListLine> Invalid);
