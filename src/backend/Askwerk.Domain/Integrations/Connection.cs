// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;

namespace Askwerk.Domain.Integrations;

/// <summary>What a connection is for (W36, AWK-49); the wizard sets up what each needs.</summary>
public static class ConnectionGoals
{
    /// <summary>Answers go to the other system: a webhook, or it reads the results.</summary>
    public const string Answers = "answers";

    /// <summary>The other system adds and invites people.</summary>
    public const string People = "people";

    /// <summary>Colleagues sign in with the company login; set up under Sign-in.</summary>
    public const string SignIn = "sign-in";

    /// <summary>Surveys show inside the other system's app; embedding with a signed hand-over.</summary>
    public const string Embed = "embed";

    /// <summary>Every goal, in the wizard's order.</summary>
    public static readonly IReadOnlyList<string> All = [Answers, People, SignIn, Embed];
}

/// <summary>What a connection may do, in words instead of scopes (W36).</summary>
public static class ConnectionPermissions
{
    /// <summary>Add and change people.</summary>
    public const string People = "people";

    /// <summary>Read surveys and answers.</summary>
    public const string Results = "results";

    /// <summary>Create and change surveys.</summary>
    public const string Surveys = "surveys";

    /// <summary>Every permission, in the wizard's order.</summary>
    public static readonly IReadOnlyList<string> All = [People, Results, Surveys];

    /// <summary>The scopes of the permissions; never users or settings.</summary>
    /// <param name="permissions">The permissions.</param>
    /// <returns>The scopes.</returns>
    public static string[] ScopesOf(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var chosen = permissions.ToHashSet(StringComparer.Ordinal);
        var scopes = new List<string>();

        if (chosen.Contains(People))
        {
            scopes.AddRange(["participants:read", "participants:write"]);
        }

        if (chosen.Contains(Results))
        {
            scopes.AddRange([ApiScopes.SurveysRead, ApiScopes.ResponsesRead]);
        }

        if (chosen.Contains(Surveys))
        {
            scopes.AddRange([ApiScopes.SurveysRead, "surveys:write"]);
        }

        return [.. ApiScopes.Allowed.Where(scopes.Contains)];
    }

    /// <summary>The permissions a set of scopes gives.</summary>
    /// <param name="scopes">The scopes.</param>
    /// <returns>The permissions.</returns>
    public static string[] Of(IReadOnlyCollection<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return [.. All.Where(permission => ScopesOf([permission]).All(scopes.Contains))];
    }
}

/// <summary>
/// A system the organisation works with (W36, AWK-49): its name and goals, and what was set up for it, an API key or
/// OAuth client for its calls and a webhook for Askwerk's. Pausing stops both directions; removing revokes the
/// credentials and deletes the webhook.
/// </summary>
public sealed class Connection
{
    /// <summary>How people from the other system are matched: by External ID, then email.</summary>
    public const string MatchExternalId = "external-id";

    /// <summary>How people from the other system are matched: by email only.</summary>
    public const string MatchEmail = "email";

    /// <summary>Most connections per organisation.</summary>
    public const int MaxPerOrganisation = 20;

    // For EF Core.
    private Connection()
    {
        Name = "";
        Goals = [];
        MatchBy = MatchExternalId;
    }

    /// <summary>Gets the id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the organisation; row-level security key.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets what the organisation calls it, e.g. Acme CRM.</summary>
    public string Name { get; private set; }

    /// <summary>Gets what it is for (<see cref="ConnectionGoals"/>).</summary>
    public string[] Goals { get; private set; }

    /// <summary>Gets how people are matched: <see cref="MatchExternalId"/> or <see cref="MatchEmail"/>.</summary>
    public string MatchBy { get; private set; }

    /// <summary>Gets the API key its calls use, when it signs in with a key.</summary>
    public Guid? ApiKeyId { get; private set; }

    /// <summary>Gets the OAuth client its calls use, when it signs in with OAuth.</summary>
    public Guid? OAuthClientId { get; private set; }

    /// <summary>Gets the webhook Askwerk calls, when it gets answers that way.</summary>
    public Guid? WebhookId { get; private set; }

    /// <summary>Gets when it was paused; null while it works.</summary>
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>Gets who paused it.</summary>
    public Guid? PausedBy { get; private set; }

    /// <summary>Gets who created it.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>Gets when it was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a connection.</summary>
    /// <param name="id">New id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="goals">What it is for; at least one.</param>
    /// <param name="matchBy">How people are matched.</param>
    /// <param name="createdBy">Who creates it.</param>
    /// <param name="now">Creation time.</param>
    /// <returns>The connection.</returns>
    public static Connection Create(Guid id, Guid tenantId, string name, IReadOnlyCollection<string> goals, string? matchBy, Guid createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(goals);

        if (goals.Count == 0 || goals.Any(goal => !ConnectionGoals.All.Contains(goal)))
        {
            throw new DomainValidationException("goals", "goals-invalid");
        }

        var connection = new Connection { Id = id, TenantId = tenantId, Goals = [.. ConnectionGoals.All.Where(goals.Contains)], CreatedBy = createdBy, CreatedAt = now };
        connection.Change(name, matchBy);

        return connection;
    }

    /// <summary>Changes the name or how people are matched.</summary>
    /// <param name="name">The new name; unchanged when null.</param>
    /// <param name="matchBy">How people are matched; unchanged when null.</param>
    public void Change(string? name, string? matchBy)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainValidationException("name", "name-required");
            }

            if (name.Trim().Length > 100)
            {
                throw new DomainValidationException("name", "name-too-long");
            }

            Name = name.Trim();
        }

        if (matchBy is not null)
        {
            MatchBy = matchBy is MatchExternalId or MatchEmail ? matchBy : throw new DomainValidationException("matchBy", "match-invalid");
        }
    }

    /// <summary>Links what was set up for it.</summary>
    /// <param name="apiKeyId">The API key.</param>
    /// <param name="oauthClientId">The OAuth client.</param>
    /// <param name="webhookId">The webhook.</param>
    public void Link(Guid? apiKeyId, Guid? oauthClientId, Guid? webhookId)
    {
        ApiKeyId = apiKeyId;
        OAuthClientId = oauthClientId;
        WebhookId = webhookId;
    }

    /// <summary>Pauses it, or lets it work again.</summary>
    /// <param name="paused">Whether it is paused.</param>
    /// <param name="by">Who pauses it.</param>
    /// <param name="now">Now.</param>
    public void Pause(bool paused, Guid by, DateTimeOffset now)
    {
        PausedAt = paused ? PausedAt ?? now : null;
        PausedBy = paused ? PausedBy ?? by : null;
    }
}
