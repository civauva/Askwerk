// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Api.App.Developers;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>The Developers hub (AWK-34, W27): the API's address, a trial key, the Postman collection and the example.</summary>
public sealed class DevelopersApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Hub_GivesTheAddress_ATrialKeyThatReads_ThePostmanCollection_AndTheExample()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var info = await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/developers"), Cancel);
        var trial = await owner.PostAsync(Url("/api/app/api-keys/trial"), null, Cancel);
        var created = await trial.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        using var integration = await factory.CreateReadyClientAsync();
        integration.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.GetProperty("secret").GetString());
        var surveys = await integration.GetAsync(Url("/api/v1/surveys"), Cancel);
        var webhooks = await integration.GetAsync(Url("/api/v1/webhooks"), Cancel);
        var postman = await owner.GetAsync(Url("/api/app/developers/postman"), Cancel);
        var collection = await postman.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var example = await owner.GetAsync(Url("/api/app/developers/examples/sql-server-sync"), Cancel);
        using var zip = new ZipArchive(await example.Content.ReadAsStreamAsync(Cancel));

        var baseUrl = info.GetProperty("baseUrl").GetString()!;
        Assert.Equal($"{baseUrl}/api/v1", info.GetProperty("apiUrl").GetString());
        Assert.Equal(HttpStatusCode.Created, trial.StatusCode);
        var key = created.GetProperty("key");
        Assert.Equal(["surveys:read", "responses:read"], key.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.InRange(key.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(61));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden), (surveys.StatusCode, webhooks.StatusCode));
        Assert.Contains("postman_collection.json", postman.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
        Assert.Equal(baseUrl, collection.GetProperty("variable")[0].GetProperty("value").GetString());
        var folders = collection.GetProperty("item").EnumerateArray().Select(folder => folder.GetProperty("name").GetString()).ToList();
        Assert.Contains("Surveys", folders);
        Assert.Contains("Responses", folders);
        Assert.Equal(19, collection.GetProperty("item").EnumerateArray().Sum(folder => folder.GetProperty("item").GetArrayLength()));
        Assert.Contains(zip.Entries, entry => entry.FullName == "askwerk-sql-server-sync/Program.cs");
        Assert.Contains(zip.Entries, entry => entry.FullName == "askwerk-sql-server-sync/README.md");
    }

    [Fact]
    public void Postman_NamesAreasAndPathVariables()
    {
        var collection = PostmanExport.From(DevelopersController.OpenApi(), "https://surveys.acme.at");
        var responses = collection["item"]!.AsArray().First(folder => folder!["name"]!.GetValue<string>() == "Responses")!;
        var request = responses["item"]!.AsArray().Select(item => item!["request"]!).First(item => item["url"]!["raw"]!.GetValue<string>().EndsWith("/responses", StringComparison.Ordinal));

        Assert.Equal("{{baseUrl}}/api/v1/surveys/:surveyId/responses", request["url"]!["raw"]!.GetValue<string>());
        Assert.Equal("surveyId", request["url"]!["variable"]![0]!["key"]!.GetValue<string>());
        Assert.Equal("Askwerk API · surveys.acme.at", collection["info"]!["name"]!.GetValue<string>());
    }
}
