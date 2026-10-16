// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Askwerk.Tests.Integration;
using Npgsql;

namespace Askwerk.Tests.Api;

/// <summary>Surveys in the App API (AWK-15 step 3, W6): list per organisation, create, draft, publish.</summary>
public sealed class SurveysApiTests(PostgresServer server)
{
    private static readonly string[] GermanAndEnglish = ["de", "en"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static object NewSurvey(string title = "Customer satisfaction 2026") => new { title, languages = GermanAndEnglish, defaultLanguage = "de" };

    /// <summary>A definition that passes the publish checks: one page, one rating question in both languages.</summary>
    private static JsonObject PublishableDefinition(string variable = "SAT_1") => new()
    {
        ["schemaVersion"] = 1,
        ["languages"] = new JsonArray("de", "en"),
        ["defaultLanguage"] = "de",
        ["pages"] = new JsonArray(new JsonObject
        {
            ["id"] = "p1",
            ["questions"] = new JsonArray(new JsonObject
            {
                ["id"] = "q1",
                ["variable"] = variable,
                ["type"] = "rating",
                ["text"] = new JsonObject { ["de"] = "Wie zufrieden sind Sie?", ["en"] = "How satisfied are you?" },
                ["required"] = true,
                ["scale"] = new JsonObject { ["min"] = 1, ["max"] = 5 },
            }),
        }),
    };

    internal static async Task<HttpClient> SignedInOwnerAsync(AskwerkApiFactory factory)
    {
        var client = await factory.CreateReadyClientAsync();
        await AccountApiTests.OwnerWithTwoFactorAsync(client);

        return client;
    }

    internal static async Task<string> CreateSurveyAsync(HttpClient client, string title = "Customer satisfaction 2026")
    {
        var created = await client.PostAsJsonAsync(Url("/api/app/surveys"), NewSurvey(title), Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await JsonAsync(created)).GetProperty("id").GetString()!;
    }

    internal static async Task<object?> ScalarAsync(AskwerkApiFactory factory, string sql)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteScalarAsync(Cancel);
    }

    [Fact]
    public async Task Create_GivesADraftWithOneEmptyPage()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);

        var id = await CreateSurveyAsync(client);
        var draft = await client.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/draft"), Cancel);

        Assert.Equal("Customer satisfaction 2026", draft.GetProperty("title").GetString());
        Assert.Equal(1, draft.GetProperty("versionNumber").GetInt32());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("published").ValueKind);
        var definition = draft.GetProperty("definition");
        Assert.Equal("de", definition.GetProperty("defaultLanguage").GetString());
        Assert.Equal(["de", "en"], definition.GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
        Assert.Equal(0, definition.GetProperty("pages")[0].GetProperty("questions").GetArrayLength());
    }

    [Theory]
    [InlineData("  ", "de", "title", "title-required")]
    [InlineData("Survey", "fr", "defaultLanguage", "default-language-not-offered")]
    public async Task Create_Invalid_IsRefusedAtTheField(string title, string defaultLanguage, string field, string code)
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);

        var created = await client.PostAsJsonAsync(Url("/api/app/surveys"), new { title, languages = GermanAndEnglish, defaultLanguage }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(code, (await JsonAsync(created)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task List_ShowsTheOrganisationsSurveysNewestChangeFirst_WithStatusAndSearch()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var first = await CreateSurveyAsync(client, "Employee pulse Q4");
        var second = await CreateSurveyAsync(client, "Event feedback Vienna");
        await client.PutAsJsonAsync(Url($"/api/app/surveys/{first}/draft"), new { definition = PublishableDefinition() }, Cancel);
        (await client.PostAsync(Url($"/api/app/surveys/{first}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        var all = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);
        var published = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys?status=published"), Cancel);
        var search = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys?search=vienna"), Cancel);

        var items = all.GetProperty("items");
        Assert.Equal([first, second], items.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(("published", 1, false), (items[0].GetProperty("status").GetString(), items[0].GetProperty("publishedVersion").GetInt32(), items[0].GetProperty("hasChanges").GetBoolean()));
        Assert.Equal("draft", items[1].GetProperty("status").GetString());
        Assert.Equal("Anna Berger", items[1].GetProperty("createdBy").GetString());
        Assert.Equal(["de", "en"], items[1].GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
        Assert.Equal(new Dictionary<string, int> { ["all"] = 2, ["draft"] = 1, ["published"] = 1, ["closed"] = 0 }, all.GetProperty("counts").Deserialize<Dictionary<string, int>>());
        Assert.Equal([first], published.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal([second], search.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task List_PagesWithTheTotal_SortedByTheChosenColumn()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        await CreateSurveyAsync(client, "Bravo");
        await CreateSurveyAsync(client, "Alpha");
        await CreateSurveyAsync(client, "Charlie");

        var first = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys?sort=title&limit=2"), Cancel);
        var second = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys?sort=title&limit=2&offset=2"), Cancel);
        var backwards = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys?sort=-title"), Cancel);

        Assert.Equal(["Alpha", "Bravo"], first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()));
        Assert.Equal(["Charlie"], second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()));
        Assert.Equal((3, 3), (first.GetProperty("total").GetInt32(), second.GetProperty("total").GetInt32()));
        Assert.Equal(["Charlie", "Bravo", "Alpha"], backwards.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task List_InAnotherOrganisation_DoesNotShowThisOrganisationsSurveys()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);
        var other = Guid.CreateVersion7();
        await ScalarAsync(factory, $"""
            INSERT INTO tenant (id, name, slug, created_at) VALUES ('{other}', 'Acme Schweiz AG', 'acme-ch', now());
            INSERT INTO membership (id, tenant_id, user_id, role, created_at) SELECT gen_random_uuid(), '{other}', id, 'Owner', now() FROM app_user;
            """);

        (await client.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = other }, Cancel)).EnsureSuccessStatusCode();
        var list = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);
        var draft = await client.GetAsync(Url($"/api/app/surveys/{id}/draft"), Cancel);

        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
    }

    [Fact]
    public async Task SaveDraft_KeepsTheDefinition_AndUpdatesTheLanguages()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);
        var definition = PublishableDefinition();
        definition["languages"] = new JsonArray("de");

        var saved = await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition }, Cancel);
        var draft = await client.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/draft"), Cancel);
        var list = await client.GetFromJsonAsync<JsonElement>(Url("/api/app/surveys"), Cancel);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal("SAT_1", draft.GetProperty("definition").GetProperty("pages")[0].GetProperty("questions")[0].GetProperty("variable").GetString());
        Assert.Equal(["de"], list.GetProperty("items")[0].GetProperty("languages").EnumerateArray().Select(language => language.GetString()));
    }

    [Fact]
    public async Task SaveDraft_NotADefinition_IsRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);

        var saved = await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = new { pages = "none" } }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        Assert.Equal("definition-invalid", (await JsonAsync(saved)).GetProperty("errors").GetProperty("definition")[0].GetString());
    }

    [Fact]
    public async Task Publish_FreezesTheVersion_StartsTheNextDraft_AndIndexesTheQuestions()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);
        await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = PublishableDefinition() }, Cancel);

        var published = await client.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel);
        var draft = await client.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/draft"), Cancel);

        var body = await JsonAsync(published);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(1, body.GetProperty("versionNumber").GetInt32());
        var code = (string)(await ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{id}'"))!;
        Assert.Equal($"https://surveys.acme.at/s/{code}", body.GetProperty("links")[0].GetProperty("url").GetString());
        Assert.Equal(2, draft.GetProperty("versionNumber").GetInt32());
        Assert.Equal("SAT_1", draft.GetProperty("published").GetProperty("pages")[0].GetProperty("questions")[0].GetProperty("variable").GetString());
        Assert.Equal(1L, await ScalarAsync(factory, $"SELECT count(*) FROM survey_question q JOIN survey_version v ON v.id = q.survey_version_id WHERE v.survey_id = '{id}' AND q.variable_name = 'SAT_1'"));
        Assert.Equal(1L, await ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'survey.published'"));
    }

    [Fact]
    public async Task Publish_WithFindings_IsRefusedWithThem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);

        var published = await client.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel);

        var body = await JsonAsync(published);
        Assert.Equal(HttpStatusCode.Conflict, published.StatusCode);
        Assert.Equal("publish-checks-failed", body.GetProperty("code").GetString());
        Assert.NotEqual(0, body.GetProperty("issues").GetArrayLength());
    }

    [Fact]
    public async Task Publish_AfterARename_RecordsTheVariableMapping()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);
        await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = PublishableDefinition("SAT_1") }, Cancel);
        (await client.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = PublishableDefinition("SATISFACTION") }, Cancel);
        (await client.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        Assert.Equal("SAT_1>SATISFACTION@2", await ScalarAsync(factory, $"SELECT from_name || '>' || to_name || '@' || since_version FROM variable_mapping WHERE survey_id = '{id}'"));
    }

    [Fact]
    public async Task PublishedVersion_CannotBeChanged_EvenDirectlyInTheDatabase()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);
        await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = PublishableDefinition() }, Cancel);
        (await client.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        var change = () => ScalarAsync(factory, $"UPDATE survey_version SET definition = '{{}}' WHERE survey_id = '{id}' AND state = 'published'");

        await Assert.ThrowsAsync<PostgresException>(change);
    }

    [Fact]
    public async Task Viewer_SeesTheList_ButCannotCreateOrChange()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);
        var id = await CreateSurveyAsync(client);
        await ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");

        var list = await client.GetAsync(Url("/api/app/surveys"), Cancel);
        var created = await client.PostAsJsonAsync(Url("/api/app/surveys"), NewSurvey(), Cancel);
        var saved = await client.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = PublishableDefinition() }, Cancel);
        var published = await client.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], [created.StatusCode, saved.StatusCode, published.StatusCode]);
    }

    [Fact]
    public async Task SignedOut_GetsNothing()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await factory.CreateReadyClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url("/api/app/surveys"), Cancel)).StatusCode);
    }

    [Fact]
    public async Task Create_IsAudited()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var client = await SignedInOwnerAsync(factory);

        await CreateSurveyAsync(client);

        Assert.Equal(1L, await ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'survey.created' AND tenant_id IS NOT NULL"));
    }
}
