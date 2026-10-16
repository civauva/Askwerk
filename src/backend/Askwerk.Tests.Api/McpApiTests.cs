// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>
/// The MCP server (AWK-32, C32 C): JSON-RPC 2.0 over POST /mcp with a key or an OAuth token; four tools that do what
/// the Public API does with the same scopes, never more.
/// </summary>
public sealed class McpApiTests(PostgresServer server)
{
    private static readonly string[] ReadOnlySurveys = ["surveys:read"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<HttpClient> CallerAsync(AskwerkApiFactory factory, HttpClient owner, string[]? scopes = null)
    {
        var (secret, _) = await ApiKeysApiTests.CreateKeyAsync(owner, scopes);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object? parameters = null, int id = 1)
    {
        var answer = await client.PostAsJsonAsync(Url("/mcp"), new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }, Cancel);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        return await answer.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private static Task<JsonElement> CallAsync(HttpClient client, string tool, object arguments) =>
        RpcAsync(client, "tools/call", new { name = tool, arguments });

    private static JsonElement TextOf(JsonElement answer) => JsonDocument.Parse(answer.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;

    [Fact]
    public async Task Initialize_AndToolsList_DescribeTheServer()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var caller = await CallerAsync(factory, owner);

        var initialized = (await RpcAsync(caller, "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } })).GetProperty("result");
        var notified = await caller.PostAsJsonAsync(Url("/mcp"), new { jsonrpc = "2.0", method = "notifications/initialized" }, Cancel);
        var tools = (await RpcAsync(caller, "tools/list")).GetProperty("result").GetProperty("tools");

        Assert.Equal(("2025-06-18", "askwerk"), (initialized.GetProperty("protocolVersion").GetString(), initialized.GetProperty("serverInfo").GetProperty("name").GetString()));
        Assert.True(initialized.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal(HttpStatusCode.Accepted, notified.StatusCode);
        Assert.Equal(["get_export", "get_survey", "list_surveys", "start_export"], tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Order());
        Assert.Equal("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ListAndGetSurvey_AnswerAsThePublicApiDoes()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        using var caller = await CallerAsync(factory, owner);

        var list = await CallAsync(caller, "list_surveys", new { });
        var one = await CallAsync(caller, "get_survey", new { surveyId = id });
        var missing = await CallAsync(caller, "get_survey", new { surveyId = Guid.NewGuid() });

        Assert.Equal("Customer satisfaction 2026", TextOf(list).GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(id, TextOf(one).GetProperty("id").GetString());
        Assert.False(one.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.True(missing.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("survey-not-found", missing.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAndGetExport_WorkWithResponsesRead()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var caller = await CallerAsync(factory, owner);

        var started = TextOf(await CallAsync(caller, "start_export", new { surveyId = id, format = "csv" }));
        var polled = TextOf(await CallAsync(caller, "get_export", new { exportId = started.GetProperty("id").GetString() }));

        Assert.Equal(("queued", "csv"), (started.GetProperty("status").GetString(), started.GetProperty("format").GetString()));
        Assert.Equal(started.GetProperty("id").GetString(), polled.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ATool_BeyondTheKeysScopes_IsRefused_AndNoKeyNoServer()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        using var caller = await CallerAsync(factory, owner, ReadOnlySurveys);
        using var anonymous = factory.CreateClient();

        var refused = await CallAsync(caller, "start_export", new { surveyId = Guid.NewGuid(), format = "csv" });
        var unknown = await RpcAsync(caller, "resources/list");
        var noKey = await anonymous.PostAsJsonAsync(Url("/mcp"), new { jsonrpc = "2.0", id = 1, method = "tools/list" }, Cancel);

        Assert.True(refused.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("responses:read", refused.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(-32601, unknown.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
    }

    [Fact]
    public async Task AnOAuthToken_WorksToo()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var (clientId, secret, _) = await OAuthApiTests.CreateClientAsync(owner);
        using var caller = factory.CreateClient();
        caller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await OAuthApiTests.AccessTokenAsync(factory, clientId, secret));

        var tools = await RpcAsync(caller, "tools/list");

        Assert.Equal(4, tools.GetProperty("result").GetProperty("tools").GetArrayLength());
    }
}
