// SPDX-License-Identifier: Apache-2.0
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Askwerk.Sdk;

/// <summary>A client of the Askwerk Public API. Give it an HttpClient of your own to control its lifetime and handlers.</summary>
public sealed class AskwerkClient : IDisposable
{
    private readonly HttpClient? _owned;

    /// <summary>Creates a client.</summary>
    /// <param name="baseUrl">The Askwerk address, e.g. https://surveys.example.org.</param>
    /// <param name="auth">An API key or an OAuth client: <see cref="AskwerkAuth.ApiKey"/> or <see cref="AskwerkAuth.ClientCredentials"/>.</param>
    /// <param name="http">An HttpClient to use; one of its own when null.</param>
    public AskwerkClient(Uri baseUrl, AskwerkAuth auth, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(auth);
        _owned = http is null ? new HttpClient() : null;
        var transport = new Transport(http ?? _owned!, baseUrl, auth);
        Surveys = new SurveysApi(transport);
        Responses = new ResponsesApi(transport);
        Exports = new ExportsApi(transport);
        Webhooks = new WebhooksApi(transport);
        Users = new UsersApi(transport);
        Participants = new ParticipantsApi(transport);
    }

    /// <summary>Gets the surveys.</summary>
    public SurveysApi Surveys { get; }

    /// <summary>Gets the responses and codebooks.</summary>
    public ResponsesApi Responses { get; }

    /// <summary>Gets the exports.</summary>
    public ExportsApi Exports { get; }

    /// <summary>Gets the webhooks.</summary>
    public WebhooksApi Webhooks { get; }

    /// <summary>Gets the provisioned users (scope users:manage).</summary>
    public UsersApi Users { get; }

    /// <summary>Gets a survey's participants (scopes participants:read and participants:write).</summary>
    public ParticipantsApi Participants { get; }

    /// <inheritdoc/>
    public void Dispose() => _owned?.Dispose();
}

/// <summary>How the SDK authenticates: an API key, or an OAuth client whose access tokens it fetches and renews.</summary>
public abstract class AskwerkAuth
{
    private protected AskwerkAuth()
    {
    }

    internal virtual bool CanRenew => false;

    /// <summary>An API key (awk_…) from Organisation → API.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The authentication.</returns>
    public static AskwerkAuth ApiKey(string key) => new Key(key);

    /// <summary>An OAuth client (client credentials); tokens are fetched when needed and renewed before they expire.</summary>
    /// <param name="clientId">The client id (cl_…).</param>
    /// <param name="clientSecret">The client secret.</param>
    /// <param name="scope">Space-separated scopes, a subset of the client's; all of them when null.</param>
    /// <returns>The authentication.</returns>
    public static AskwerkAuth ClientCredentials(string clientId, string clientSecret, string? scope = null) => new Client(clientId, clientSecret, scope);

    internal abstract Task<string> BearerAsync(HttpClient http, string baseUrl, bool renew, CancellationToken cancellationToken);

    private sealed class Key(string key) : AskwerkAuth
    {
        internal override Task<string> BearerAsync(HttpClient http, string baseUrl, bool renew, CancellationToken cancellationToken) => Task.FromResult(key);
    }

    private sealed class Client(string clientId, string clientSecret, string? scope) : AskwerkAuth
    {
        private string? _token;
        private DateTimeOffset _until;

        internal override bool CanRenew => true;

        internal override async Task<string> BearerAsync(HttpClient http, string baseUrl, bool renew, CancellationToken cancellationToken)
        {
            if (!renew && _token is not null && _until > DateTimeOffset.UtcNow)
            {
                return _token;
            }

            var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret };

            if (scope is not null)
            {
                form["scope"] = scope;
            }

            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(new Uri(baseUrl + "/api/v1/oauth/token"), content, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw await AskwerkException.FromAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var token = (await response.Content.ReadFromJsonAsync<TokenAnswer>(Transport.Json, cancellationToken).ConfigureAwait(false))!;
            _token = token.AccessToken;
            _until = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);

            return _token;
        }

        private sealed record TokenAnswer([property: JsonPropertyName("access_token")] string AccessToken, [property: JsonPropertyName("expires_in")] int ExpiresIn);
    }
}

/// <summary>An error answer of the Public API, with its problem details.</summary>
public sealed class AskwerkException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AskwerkException"/> class.</summary>
    public AskwerkException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AskwerkException"/> class.</summary>
    /// <param name="message">The message.</param>
    public AskwerkException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AskwerkException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public AskwerkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the HTTP status.</summary>
    public int Status { get; init; }

    /// <summary>Gets the stable problem type, e.g. https://askwerk.org/problems/not-published.</summary>
    public string? Type { get; init; }

    /// <summary>Gets the problem's title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets what to do.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the trace id that helps an administrator find the call in the logs.</summary>
    public string? TraceId { get; init; }

    internal static async Task<AskwerkException> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? Read(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        JsonElement problem = default;

        try
        {
            problem = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            // not JSON: only the status is known
        }

        var title = Read(problem, "title") ?? Read(problem, "error_description");

        return new AskwerkException(title ?? $"HTTP {(int)response.StatusCode}")
        {
            Status = (int)response.StatusCode,
            Type = Read(problem, "type") ?? Read(problem, "error"),
            Title = title,
            Detail = Read(problem, "detail"),
            TraceId = Read(problem, "traceId"),
        };
    }
}

/// <summary>Checks the signature of webhook deliveries.</summary>
public static class AskwerkWebhooks
{
    /// <summary>
    /// Checks a delivery's Askwerk-Signature header: HMAC-SHA256 of "{t}.{body}" with the webhook's secret, at most
    /// <paramref name="tolerance"/> old (5 minutes when null). Pass the raw request body.
    /// </summary>
    /// <param name="secret">The webhook's secret (whsec_…).</param>
    /// <param name="header">The Askwerk-Signature header.</param>
    /// <param name="body">The raw body.</param>
    /// <param name="tolerance">How old a delivery may be.</param>
    /// <returns>Whether the delivery is genuine and fresh.</returns>
    public static bool Verify(string secret, string header, string body, TimeSpan? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(header);
        var parts = header.Split(',').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).DistinctBy(part => part[0], StringComparer.Ordinal).ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);

        if (!parts.TryGetValue("t", out var t) || !parts.TryGetValue("v1", out var v1) || !long.TryParse(t, out var seconds)
            || Math.Abs((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds)).TotalSeconds) > (tolerance ?? TimeSpan.FromMinutes(5)).TotalSeconds)
        {
            return false;
        }

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{body}"))).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(v1));
    }
}

/// <summary>Exports, with waiting until a file is ready.</summary>
public sealed partial class ExportsApi
{
    /// <summary>Polls the export until it is ready; throws when it failed or expired, or after the timeout.</summary>
    /// <param name="exportId">The export.</param>
    /// <param name="interval">Between polls; 2 seconds when null.</param>
    /// <param name="timeout">At most; 10 minutes when null.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The ready export.</returns>
    public async Task<PublicExport> WaitUntilReadyAsync(Guid exportId, TimeSpan? interval = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var until = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));

        while (true)
        {
            var current = await GetAsync(exportId, cancellationToken).ConfigureAwait(false);

            if (current.Status == "ready")
            {
                return current;
            }

            if (current.Status is "failed" or "expired")
            {
                throw new AskwerkException(current.Error?.Title ?? $"The export {current.Status}.") { Status = 409, Type = current.Error?.Type ?? current.Status, Title = current.Error?.Title, Detail = current.Error?.Detail };
            }

            if (DateTimeOffset.UtcNow > until)
            {
                throw new AskwerkException("The export was not ready in time.") { Status = 408 };
            }

            await Task.Delay(interval ?? TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }
}
