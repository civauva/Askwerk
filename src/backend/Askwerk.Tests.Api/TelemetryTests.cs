// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Askwerk.Tests.Api;

public sealed class TelemetryTests
{
    private const string ClientTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task Post_ValidBatch_Returns202()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/app/telemetry", UriKind.Relative), ValidBatch(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Post_ErrorRecord_IsLoggedAsErrorWithServiceAndClientTraceId()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(new Uri("/api/app/telemetry", UriKind.Relative), ValidBatch(), TestContext.Current.CancellationToken);

        var record = Assert.Single(factory.Logs.Records, log => log.Category == "Askwerk.Frontend" && log.Level == LogLevel.Error);
        Assert.Equal("askwerk-designer", record.State["Service"]);
        Assert.Equal(ClientTraceId, record.State["ClientTraceId"]);
        Assert.Equal("api.failed", record.State["Name"]);
    }

    [Fact]
    public async Task Post_UnknownService_Returns400ProblemDetails()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();
        var batch = new { service = "somewhere-else", records = Array.Empty<object>() };

        var response = await client.PostAsJsonAsync(new Uri("/api/app/telemetry", UriKind.Relative), batch, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_MoreThan50Records_Returns400()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();
        var records = Enumerable.Range(0, 51).Select(_ => new { kind = "metric", name = "web.lcp", value = 1200.0, route = "/" }).ToArray();

        var response = await client.PostAsJsonAsync(new Uri("/api/app/telemetry", UriKind.Relative), new { service = "designer", records }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmailAddressInMessage_IsNotLogged()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();
        var batch = new
        {
            service = "designer",
            records = new[] { new { kind = "log", level = "error", name = "ui.error", message = "failed for jane.doe@example.com", route = "/", traceId = ClientTraceId } },
        };

        await client.PostAsJsonAsync(new Uri("/api/app/telemetry", UriKind.Relative), batch, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(factory.Logs.Records, log => log.Message.Contains("jane.doe@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_TooManyBatchesFromOneClient_Returns429()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var request = 0; request < 61; request++)
        {
            var response = await client.PostAsJsonAsync(new Uri("/api/app/telemetry", UriKind.Relative), ValidBatch(), TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Request_ProducesAServerSpan()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        // The server span is exported when it ends, which can be just after the response arrives.
        var exported = await Eventually.TrueAsync(() => factory.Spans.ToArray().Any(span => span.DisplayName.Contains("/health/live", StringComparison.Ordinal)));
        Assert.True(exported);
    }

    private static object ValidBatch() => new
    {
        service = "designer",
        records = new object[]
        {
            new { kind = "metric", name = "web.lcp", value = 1200.0, route = "/" },
            new { kind = "log", level = "error", name = "api.failed", message = "GET /api/app/status 503", route = "/", traceId = ClientTraceId },
        },
    };
}
