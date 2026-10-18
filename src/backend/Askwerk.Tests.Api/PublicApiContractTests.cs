// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;

namespace Askwerk.Tests.Api;

public sealed class PublicApiContractTests
{
    [Fact]
    public async Task PublicOpenApiDocument_MatchesTheCommittedContract()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken);

        // A change to docs/api/openapi.json is a Public API change (agents/public-api.md).
        Snapshot.AssertJsonMatches("docs/api/openapi.json", document);
    }

    [Fact]
    public async Task AppOpenApiDocument_MatchesTheDocumentTheFrontendClientIsGeneratedFrom()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync(new Uri("/api/app/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken);

        // packages/app-client generates its types from this file; a mismatch means the client is stale.
        Snapshot.AssertJsonMatches("src/frontend/packages/app-client/openapi.json", document);
    }

    [Fact]
    public async Task PublicOpenApiDocument_HasTheFirstPublicEndpoints_BehindABearerKey()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!.AsObject().Select(path => path.Key).Order(StringComparer.Ordinal);

        // AWK-25 (W14 part 2), AWK-32 (C32 A webhooks, B the token endpoint), AWK-49 (W36 participants) and AWK-67 (contacts): these, and nothing else, are public.
        Assert.Equal(
            [
                "/api/v1/contacts", "/api/v1/contacts/{id}", "/api/v1/exports/{exportId}", "/api/v1/exports/{exportId}/file", "/api/v1/oauth/token", "/api/v1/surveys", "/api/v1/surveys/{surveyId}", "/api/v1/surveys/{surveyId}/codebook", "/api/v1/surveys/{surveyId}/exports", "/api/v1/surveys/{surveyId}/participants", "/api/v1/surveys/{surveyId}/responses", "/api/v1/users", "/api/v1/users/{externalId}",
                "/api/v1/webhooks", "/api/v1/webhooks/{id}", "/api/v1/webhooks/{id}/deliveries", "/api/v1/webhooks/{id}/deliveries/{deliveryId}/replay",
            ],
            paths);
        Assert.Equal("bearer", document["components"]!["securitySchemes"]!["ApiKey"]!["scheme"]!.GetValue<string>());
    }

    [Fact]
    public async Task PublicOpenApiDocument_ContainsNoAppApiRoutes()
    {
        await using var factory = AskwerkApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var paths = JsonNode.Parse(document)!["paths"]!.AsObject();

        Assert.DoesNotContain(paths, path => path.Key.StartsWith("/api/app/", StringComparison.Ordinal));
    }
}
