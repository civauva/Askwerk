// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Askwerk.Sdk;

/// <summary>Requests, authentication, errors, retries on 429 and paging: the hand-written part of the SDK.</summary>
internal sealed class Transport(HttpClient http, Uri baseUrl, AskwerkAuth auth)
{
    private const int MaxRetries = 3;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string _base = baseUrl.ToString().TrimEnd('/');

    public async Task<T> SendAsync<T>(HttpMethod method, string path, (string Name, object? Value)[] query, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, query, body, idempotencyKey, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task SendAsync(HttpMethod method, string path, (string Name, object? Value)[] query, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, query, body, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> SendForBytesAsync(HttpMethod method, string path, (string Name, object? Value)[] query, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, query, body, idempotencyKey, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async IAsyncEnumerable<TItem> ByCursorAsync<TPage, TItem>(Func<string?, Task<TPage>> page, Func<TPage, IReadOnlyList<TItem>> items, Func<TPage, string?> nextCursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await page(cursor).ConfigureAwait(false);

            foreach (var item in items(next))
            {
                yield return item;
            }

            cursor = nextCursor(next);
        }
        while (cursor is not null);
    }

    public static async IAsyncEnumerable<TItem> BySequenceAsync<TPage, TItem>(long since, Func<long, Task<TPage>> page, Func<TPage, IReadOnlyList<TItem>> items, Func<TPage, long> nextSince, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await page(since).ConfigureAwait(false);

            if (items(next).Count == 0)
            {
                yield break;
            }

            foreach (var item in items(next))
            {
                yield return item;
            }

            since = nextSince(next);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, (string Name, object? Value)[] query, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var url = new Uri(_base + path + QueryString(query));

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.BearerAsync(http, _base, attempt > 0, cancellationToken).ConfigureAwait(false));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            if (idempotencyKey is not null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
                {
                    await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && auth.CanRenew && attempt == 0)
                {
                    continue;
                }

                throw await AskwerkException.FromAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string QueryString((string Name, object? Value)[] query)
    {
        var parts = query
            .Where(part => part.Value is not null)
            .Select(part => Uri.EscapeDataString(part.Name) + "=" + Uri.EscapeDataString(Convert.ToString(part.Value, CultureInfo.InvariantCulture) ?? ""))
            .ToList();

        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);
    }
}
