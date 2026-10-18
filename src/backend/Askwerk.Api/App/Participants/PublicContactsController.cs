// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel.DataAnnotations;
using Askwerk.Api.Identity;
using Askwerk.Api.Integrations;
using Askwerk.Application.Auditing;
using Askwerk.Application.Participants;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Participants;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Public API: the organisation's contacts (AWK-67), so a CRM keeps the address book in step. One PUT adds or updates
/// a person, found by External ID, else email. Who unsubscribed stays unsubscribed: the API never subscribes anyone again.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[Route("api/v1/contacts")]
[Produces("application/json")]
[PublicValidation("contact-invalid")]
public sealed class PublicContactsController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    /// <summary>Lists the contacts with their groups, fields and whether they unsubscribed or bounced, oldest first.</summary>
    /// <remarks>Scope contacts:read. Pages on a cursor: pass nextCursor as cursor until it is null.</remarks>
    /// <param name="search">Part of the name, email or External ID.</param>
    /// <param name="group">Only contacts in this group.</param>
    /// <param name="externalId">Only the contact with this External ID.</param>
    /// <param name="limit">Contacts per page, at most 100; 50 when empty.</param>
    /// <param name="cursor">Where the page starts, from the previous page's nextCursor.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of contacts.</returns>
    /// <response code="200">One page of contacts.</response>
    /// <response code="400">contact-invalid: the cursor is not one.</response>
    /// <response code="401">unauthorized: no valid API key or token.</response>
    /// <response code="403">scope-missing: it lacks contacts:read.</response>
    [HttpGet]
    [Authorize(Policy = PublicApi.ContactsRead)]
    [ProducesResponseType<PublicContactPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List(string? search, string? group, string? externalId, int? limit, Guid? cursor, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = context.Contacts.AsNoTracking()
            .Where(row => (externalId == null || row.ExternalId == externalId) && (cursor == null || row.Id.CompareTo(cursor.Value) > 0));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(row => EF.Functions.ILike(row.Email, pattern) || EF.Functions.ILike(row.Name ?? "", pattern) || EF.Functions.ILike(row.ExternalId ?? "", pattern));
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            rows = rows.Where(row => row.Groups.Contains(group));
        }

        var found = await Viewed(rows.OrderBy(row => row.Id).Take(size + 1)).ToListAsync(cancellationToken);
        var page = found.Take(size).ToList();

        return Ok(new PublicContactPage([.. page.Select(row => row.ToDto())], found.Count > size ? page[^1].Contact.Id.ToString() : null));
    }

    /// <summary>Reads one contact.</summary>
    /// <remarks>Scope contacts:read.</remarks>
    /// <param name="id">The contact.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The contact.</returns>
    /// <response code="200">The contact.</response>
    /// <response code="401">unauthorized: no valid API key or token.</response>
    /// <response code="403">scope-missing: it lacks contacts:read.</response>
    /// <response code="404">contact-not-found.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PublicApi.ContactsRead)]
    [ProducesResponseType<PublicContact>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (await ViewAsync(id, cancellationToken) is not { } contact)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "contact-not-found");
        }

        return Ok(contact);
    }

    /// <summary>Adds a contact, or updates the one with the same External ID, else the same email.</summary>
    /// <remarks>
    /// Scope contacts:write. Idempotent: sending the same person again changes nothing. What is not sent is kept: an
    /// empty name, language or External ID keeps the stored one; fields and groups, when sent, replace the stored ones.
    /// Unsubscribed and bounced are read-only: someone who unsubscribed stays unsubscribed, also under a new address.
    /// </remarks>
    /// <param name="request">The person.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The contact.</returns>
    /// <response code="200">Updated: a contact with this External ID or email existed.</response>
    /// <response code="201">Added.</response>
    /// <response code="400">contact-invalid: errors names each field (email-required, email-invalid, language-invalid, too-long, too-many-groups).</response>
    /// <response code="401">unauthorized: no valid API key or token.</response>
    /// <response code="403">scope-missing: it lacks contacts:write.</response>
    /// <response code="409">contact-email-taken: the External ID found one contact and the email another.</response>
    [HttpPut]
    [Authorize(Policy = PublicApi.ContactsWrite)]
    [ProducesResponseType<PublicContact>(StatusCodes.Status200OK)]
    [ProducesResponseType<PublicContact>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Put(PublicContactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var details = new PersonDetails(request.Email ?? "", request.Name, request.Language, request.ExternalId, request.Fields);

        if (!ParticipantList.IsAddress(details.Email))
        {
            return PublicValidationAttribute.Result(HttpContext, "contact-invalid", new Dictionary<string, string[]> { ["email"] = ["email-invalid"] });
        }

        var byExternalId = details.ExternalId is { } externalId ? await context.Contacts.FirstOrDefaultAsync(row => row.ExternalId == externalId, cancellationToken) : null;
        var byEmail = await context.Contacts.FirstOrDefaultAsync(row => row.Email == details.Email, cancellationToken);

        if (byExternalId is not null && byEmail is not null && byExternalId.Id != byEmail.Id)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status409Conflict, "contact-email-taken");
        }

        var now = time.GetUtcNow();
        var tenantId = User.TenantId()!.Value;
        var contact = byExternalId ?? byEmail;
        var isNew = contact is null;

        if (contact is null)
        {
            contact = Contact.Create(Guid.CreateVersion7(now), tenantId, details, request.Groups, now);
            context.Contacts.Add(contact);
        }
        else
        {
            await KeepUnsubscribedAsync(tenantId, contact.Email, details.Email, now, cancellationToken);
            var fields = request.Fields is null ? PersonDetails.ReadFields(contact.Fields) : details.Fields;
            contact.Change(new PersonDetails(details.Email, details.Name ?? contact.Name, details.Language ?? contact.Language, details.ExternalId ?? contact.ExternalId, fields), now);

            if (request.Groups is { } groups)
            {
                contact.SetGroups(groups, now);
            }
        }

        await AuditAsync(tenantId, isNew ? "contact.added" : "contact.changed", contact.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var view = (await ViewAsync(contact.Id, cancellationToken))!;

        return isNew ? Created($"/api/v1/contacts/{contact.Id}", view) : Ok(view);
    }

    /// <summary>Removes a contact; their participants in surveys stay, without the link to them.</summary>
    /// <remarks>Scope contacts:write. An unsubscribed address stays unsubscribed after the contact is removed.</remarks>
    /// <param name="id">The contact.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Removed.</response>
    /// <response code="401">unauthorized: no valid API key or token.</response>
    /// <response code="403">scope-missing: it lacks contacts:write.</response>
    /// <response code="404">contact-not-found.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PublicApi.ContactsWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await context.Contacts.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) is not { } contact)
        {
            return PublicApi.Problem(HttpContext, StatusCodes.Status404NotFound, "contact-not-found");
        }

        await context.Participants.Where(row => row.ContactId == id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ContactId, (Guid?)null), cancellationToken);
        context.Contacts.Remove(contact);
        await AuditAsync(contact.TenantId, "contact.removed", contact.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>An address that unsubscribed stays so when the contact gets a new one: the new address is unsubscribed too.</summary>
    private async Task KeepUnsubscribedAsync(Guid tenantId, string before, string after, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (before == after || !await context.Unsubscribes.AnyAsync(row => row.Email == before, cancellationToken))
        {
            return;
        }

        if (!await context.Unsubscribes.AnyAsync(row => row.Email == after, cancellationToken))
        {
            context.Unsubscribes.Add(Unsubscribe.Create(Guid.CreateVersion7(now), tenantId, after, now));
        }
    }

    private async Task<PublicContact?> ViewAsync(Guid id, CancellationToken cancellationToken) =>
        (await Viewed(context.Contacts.AsNoTracking().Where(row => row.Id == id)).FirstOrDefaultAsync(cancellationToken))?.ToDto();

    private IQueryable<ContactRow> Viewed(IQueryable<Contact> contacts) =>
        contacts.Select(contact => new ContactRow(
            contact,
            context.Unsubscribes.Any(row => row.Email == contact.Email),
            context.Participants.Any(row => row.BouncedAt != null && (row.ContactId == contact.Id || row.Email == contact.Email))));

    private Task AuditAsync(Guid tenantId, string action, Guid contactId, CancellationToken cancellationToken)
    {
        var (kind, actorId) = User.IntegrationActor();

        return audit.WriteAsync(new AuditContent(time.GetUtcNow(), tenantId, kind, actorId, action, "Contact", contactId.ToString(), Diff: null, trace.CurrentTraceId), cancellationToken);
    }

    private sealed record ContactRow(Contact Contact, bool Unsubscribed, bool Bounced)
    {
        public PublicContact ToDto() => new(
            Contact.Id,
            Contact.Email,
            Contact.Name,
            Contact.ExternalId,
            Contact.Language,
            Contact.Groups,
            PersonDetails.ReadFields(Contact.Fields),
            Unsubscribed,
            Bounced,
            Contact.CreatedAt,
            Contact.UpdatedAt);
    }
}

/// <summary>A contact to add or update.</summary>
/// <param name="Email">Their address; finds them when no External ID is sent or none matches.</param>
/// <param name="Name">Their name, for {name}; kept when empty.</param>
/// <param name="Language">Their language as two letters, e.g. de; kept when empty.</param>
/// <param name="ExternalId">Their id in the other system; finds them first, also after an address change.</param>
/// <param name="Fields">Further fields, e.g. segment; replace the stored ones when sent.</param>
/// <param name="Groups">Their groups; replace the stored ones when sent.</param>
public sealed record PublicContactRequest(
    [Required(ErrorMessage = "email-required")][EmailAddress(ErrorMessage = "email-invalid")][StringLength(320, ErrorMessage = "too-long")] string? Email,
    [StringLength(200, ErrorMessage = "too-long")] string? Name = null,
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "language-invalid")] string? Language = null,
    [StringLength(200, ErrorMessage = "too-long")] string? ExternalId = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    [MaxLength(50, ErrorMessage = "too-many-groups")] IReadOnlyList<string>? Groups = null);

/// <summary>A contact.</summary>
/// <param name="Id">The contact.</param>
/// <param name="Email">Their address, lower case.</param>
/// <param name="Name">Their name.</param>
/// <param name="ExternalId">Their id in the other system.</param>
/// <param name="Language">Their language.</param>
/// <param name="Groups">Their groups.</param>
/// <param name="Fields">Further fields.</param>
/// <param name="Unsubscribed">They asked for no more invitations; never changed by the API.</param>
/// <param name="Bounced">An invitation to their address was refused.</param>
/// <param name="CreatedAt">Added (UTC).</param>
/// <param name="UpdatedAt">Last changed (UTC).</param>
public sealed record PublicContact(Guid Id, string Email, string? Name, string? ExternalId, string? Language, IReadOnlyList<string> Groups, IReadOnlyDictionary<string, string> Fields, bool Unsubscribed, bool Bounced, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>One page of contacts.</summary>
/// <param name="Items">Oldest first.</param>
/// <param name="NextCursor">Pass as cursor for the next page; null on the last.</param>
public sealed record PublicContactPage(IReadOnlyList<PublicContact> Items, string? NextCursor);
