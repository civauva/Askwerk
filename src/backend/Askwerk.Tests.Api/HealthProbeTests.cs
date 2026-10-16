// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using Askwerk.HealthProbe;

namespace Askwerk.Tests.Api;

public sealed class HealthProbeTests
{
    [Fact]
    public async Task RunAsync_ReadyEndpointAnswers200_ReturnsZero()
    {
        using var client = new HttpClient(new FixedResponseHandler(HttpStatusCode.OK));

        var exitCode = await Probe.RunAsync(client, Probe.DefaultUrl, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_ReadyEndpointAnswers503_ReturnsOne()
    {
        using var client = new HttpClient(new FixedResponseHandler(HttpStatusCode.ServiceUnavailable));

        var exitCode = await Probe.RunAsync(client, Probe.DefaultUrl, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_NothingListening_ReturnsOne()
    {
        using var client = new HttpClient(new FailingHandler());

        var exitCode = await Probe.RunAsync(client, Probe.DefaultUrl, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void DefaultUrl_IsTheLocalReadyEndpoint()
    {
        Assert.Equal(new Uri("http://localhost:8080/health/ready"), Probe.DefaultUrl);
    }

    private sealed class FixedResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
