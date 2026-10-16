// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;

namespace Askwerk.Tests.Api;

public sealed class StaticAppTests
{
    [Fact]
    public async Task Root_ServesTheDesignerApp()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains("<div id=\"root\"></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asset_IsServedWithItsContentType()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/assets/app.js", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ClientSideRoute_FallsBackToTheApp()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/surveys/42/edit", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains("<div id=\"root\"></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurveyLink_ServesTheRunner_NotTheDesigner()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/s/0199e000-0000-7000-8000-000000000001", UriKind.Relative), TestContext.Current.CancellationToken);
        var asset = await client.GetAsync(new Uri("/s/assets/runner.js", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains("data-app=\"runner\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
    }

    [Fact]
    public async Task HashedAssets_AreCachedForAYear_ThePageNever()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var asset = await client.GetAsync(new Uri("/assets/app.js", UriKind.Relative), TestContext.Current.CancellationToken);
        var runnerAsset = await client.GetAsync(new Uri("/s/assets/runner.js", UriKind.Relative), TestContext.Current.CancellationToken);
        var page = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
        var route = await client.GetAsync(new Uri("/surveys/42", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("public, max-age=31536000, immutable", asset.Headers.CacheControl?.ToString());
        Assert.Equal("public, max-age=31536000, immutable", runnerAsset.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", page.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", route.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task BuiltFiles_AreSentCompressed(string encoding)
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/assets/app.js", UriKind.Relative));
        request.Headers.AcceptEncoding.ParseAdd(encoding);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal([encoding], response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ApiAnswers_AreNotCompressed_SoNoSecretCanLeakThroughTheirSize()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/app/status", UriKind.Relative));
        request.Headers.AcceptEncoding.ParseAdd("br");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task UnknownApiRoute_IsNotAnsweredWithTheApp()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/app/nothing-here", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
