// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace Askwerk.Domain.Participants;

/// <summary>
/// A person in the organisation's address book (W35): invited to many surveys with the same fields. The External ID
/// links them to another system (CRM, HR) and wins over the email when people are matched.
/// </summary>
public sealed class Contact
{
    // For EF Core.
    private Contact()
    {
        Email = "";
        Fields = "{}";
        Groups = [];
    }

    /// <summary>Stable id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Lower case; unique in the organisation.</summary>
    public string Email { get; private set; }

    /// <summary>For {name}.</summary>
    public string? Name { get; private set; }

    /// <summary>Their language, e.g. de.</summary>
    public string? Language { get; private set; }

    /// <summary>Their id in another system; unique in the organisation when set.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Further fields as a JSON object of texts, e.g. {"department":"Sales"}; placeholders in messages.</summary>
    public string Fields { get; private set; }

    /// <summary>The groups they are in.</summary>
    public string[] Groups { get; private set; }

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new contact.</summary>
    public static Contact Create(Guid id, Guid tenantId, PersonDetails details, IEnumerable<string>? groups, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var contact = new Contact { Id = id, TenantId = tenantId, CreatedAt = now };
        contact.Change(details, now);
        contact.Groups = [.. (groups ?? []).Select(group => group.Trim()).Where(group => group.Length > 0).Distinct(StringComparer.Ordinal)];

        return contact;
    }

    /// <summary>Changes the details; empty texts clear them.</summary>
    public void Change(PersonDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        Email = details.Email;
        Name = details.Name;
        Language = details.Language;
        ExternalId = details.ExternalId;
        Fields = details.FieldsJson;
        UpdatedAt = now;
    }

    /// <summary>Adds the details given, keeping what is not given (an import that knows less).</summary>
    public void Merge(PersonDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var fields = PersonDetails.ReadFields(Fields);

        foreach (var (key, value) in details.Fields)
        {
            fields[key] = value;
        }

        Change(new PersonDetails(details.Email, details.Name ?? Name, details.Language ?? Language, details.ExternalId ?? ExternalId, fields), now);
    }

    /// <summary>Sets their groups.</summary>
    public void SetGroups(IEnumerable<string> groups, DateTimeOffset now)
    {
        Groups = [.. groups.Select(group => group.Trim()).Where(group => group.Length > 0).Distinct(StringComparer.Ordinal)];
        UpdatedAt = now;
    }

    /// <summary>Puts them into groups as well.</summary>
    public void Join(IEnumerable<string> groups, DateTimeOffset now)
    {
        Groups = [.. Groups.Concat(groups.Select(group => group.Trim()).Where(group => group.Length > 0)).Distinct(StringComparer.Ordinal)];
        UpdatedAt = now;
    }
}

/// <summary>What is known about a person (W35): address, name, language, External ID and further fields, cleaned up.</summary>
public sealed record PersonDetails
{
    /// <summary>Cleans the details: the email lower case, empty texts null, the language two letters, field keys lower case with underscores.</summary>
    public PersonDetails(string email, string? name, string? language, string? externalId, IReadOnlyDictionary<string, string>? fields)
    {
        ArgumentNullException.ThrowIfNull(email);
        Email = email.Trim().ToLowerInvariant();
        Name = Clean(name, 200);
        Language = Clean(language, 16)?.ToLowerInvariant();
        ExternalId = Clean(externalId, 200);
        Fields = (fields ?? new Dictionary<string, string>())
            .Select(field => (Key: FieldKey(field.Key), Value: Clean(field.Value, 1000)))
            .Where(field => field.Key.Length > 0 && field.Value is not null)
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value!, StringComparer.Ordinal);
    }

    /// <summary>Lower case.</summary>
    public string Email { get; }

    /// <summary>For {name}.</summary>
    public string? Name { get; }

    /// <summary>E.g. de.</summary>
    public string? Language { get; }

    /// <summary>Their id in another system.</summary>
    public string? ExternalId { get; }

    /// <summary>Further fields.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; }

    /// <summary>The fields as stored.</summary>
    public string FieldsJson => JsonSerializer.Serialize(Fields);

    /// <summary>A field's key as placeholders use it: "Customer no." becomes customer_no.</summary>
    public static string FieldKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = new string([.. name.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '_')]);

        while (key.Contains("__", StringComparison.Ordinal))
        {
            key = key.Replace("__", "_", StringComparison.Ordinal);
        }

        return key.Trim('_');
    }

    /// <summary>Stored fields as a dictionary.</summary>
    public static Dictionary<string, string> ReadFields(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [], StringComparer.Ordinal);

    private static string? Clean(string? text, int max) => string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, max)];
}
