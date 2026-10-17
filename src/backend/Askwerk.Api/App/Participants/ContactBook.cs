// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Participants;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Participants;

/// <summary>
/// Finds people in the organisation's address book (W35): by External ID first, then by email, so a person whose
/// address changed is still the same contact.
/// </summary>
public sealed class ContactBook
{
    private readonly Dictionary<string, Contact> byExternalId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Contact> byEmail = new(StringComparer.Ordinal);

    private ContactBook(IEnumerable<Contact> contacts)
    {
        foreach (var contact in contacts)
        {
            Add(contact);
        }
    }

    /// <summary>Loads the contacts that could be any of these people.</summary>
    /// <param name="context">A context in the organisation.</param>
    /// <param name="people">The people.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The book.</returns>
    public static async Task<ContactBook> MatchAsync(AskwerkDbContext context, IEnumerable<ParticipantInput> people, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var details = people.Select(person => person.Details()).ToList();
        var emails = details.Select(person => person.Email).ToList();
        var ids = details.Where(person => person.ExternalId is not null).Select(person => person.ExternalId!).ToList();

        return new ContactBook(await context.Contacts.Where(contact => emails.Contains(contact.Email) || (contact.ExternalId != null && ids.Contains(contact.ExternalId))).ToListAsync(cancellationToken));
    }

    /// <summary>The contact this person is, if any.</summary>
    public Contact? Find(PersonDetails person)
    {
        ArgumentNullException.ThrowIfNull(person);

        return person.ExternalId is { } id && byExternalId.TryGetValue(id, out var known) ? known : byEmail.GetValueOrDefault(person.Email);
    }

    /// <summary>Remembers a new contact, so the same person later in the list finds it.</summary>
    public void Add(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        byEmail[contact.Email] = contact;

        if (contact.ExternalId is { } id)
        {
            byExternalId[id] = contact;
        }
    }
}
