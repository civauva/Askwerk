// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;

namespace Askwerk.Tests.Api;

/// <summary>Stands in for the integrator's server behind every webhook URL (AWK-32): records each POST and answers as told.</summary>
internal sealed class FakeWebhookReceiver
{
    public List<(Uri Url, Dictionary<string, string> Headers, string Body)> Received { get; } = [];

    /// <summary>Gets the status answered next; 200 when empty.</summary>
    public Queue<HttpStatusCode> Answers { get; } = new();

    public HttpMessageHandler Handler() => new Recorder(this);

    private sealed class Recorder(FakeWebhookReceiver receiver) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);

            lock (receiver.Received)
            {
                receiver.Received.Add((request.RequestUri!, headers, body));
            }

            var status = receiver.Answers.TryDequeue(out var answer) ? answer : HttpStatusCode.OK;

            return new HttpResponseMessage(status) { RequestMessage = request };
        }
    }
}
