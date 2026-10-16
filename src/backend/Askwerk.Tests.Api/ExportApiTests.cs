// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Jobs;
using Askwerk.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// Exporting results (AWK-24, W11 C; AWK-25, W14 A): an export is asked for, prepared by a job in the chosen format
/// following the codebook and the results' filter, and then downloaded; its file is kept 7 days. Every export and
/// every download is in the audit log; viewers read results but do not export. Jobs run when a test asks.
/// </summary>
public sealed class ExportApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static Task RunJobsAsync(AskwerkApiFactory factory) => factory.Services.GetRequiredService<JobRunner>().RunDueAsync(Cancel);

    /// <summary>Asks for an export, lets its job run, and downloads the file.</summary>
    internal static async Task<HttpResponseMessage> ExportAsync(AskwerkApiFactory factory, HttpClient owner, string surveyId, object request)
    {
        var started = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{surveyId}/exports"), request, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var exportId = (await JsonAsync(started)).GetProperty("id").GetString();
        await RunJobsAsync(factory);

        return await owner.GetAsync(Url($"/api/app/exports/{exportId}/file"), Cancel);
    }

    [Fact]
    public async Task Csv_IsPreparedByAJob_ThenDownloaded()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var started = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv", values = "codes" }, Cancel);
        var queued = await JsonAsync(started);
        var exportId = queued.GetProperty("id").GetString();
        await RunJobsAsync(factory);
        var ready = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/exports/{exportId}"), Cancel);
        var file = await owner.GetAsync(Url($"/api/app/exports/{exportId}/file"), Cancel);
        var lines = Encoding.UTF8.GetString(await file.Content.ReadAsByteArrayAsync(Cancel)).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal(("queued", "csv"), (queued.GetProperty("status").GetString(), queued.GetProperty("format").GetString()));
        Assert.Equal(("ready", 3, 3), (ready.GetProperty("status").GetString(), ready.GetProperty("done").GetInt32(), ready.GetProperty("total").GetInt32()));
        Assert.Equal(("customer-satisfaction-2026.csv", "text/csv"), (ready.GetProperty("file").GetProperty("name").GetString(), ready.GetProperty("file").GetProperty("contentType").GetString()));
        Assert.True(ready.GetProperty("file").GetProperty("size").GetInt64() > 100);
        Assert.Equal("Anna Berger", ready.GetProperty("requestedBy").GetString());
        Assert.Equal(ready.GetProperty("finishedAt").GetDateTimeOffset().AddDays(7), ready.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("customer-satisfaction-2026.csv", file.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("response_id,status,version,language,started_at,completed_at,duration_seconds,SAT,SOURCE_1,SOURCE_2,SOURCE_OTHER,BEFORE,EMAIL", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.EndsWith(",4,1,0,Messe,1,", lines[1], StringComparison.Ordinal);
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'results.exported'"));
        Assert.Equal(1L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM audit_log WHERE action = 'export.downloaded'"));
    }

    [Fact]
    public async Task ASmallExport_IsReadyInSeconds_NotAtTheNextRoundOfJobs()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        factory.RunJobsByThemselves = true;
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var started = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel));
        var status = "";

        for (var poll = 0; poll < 50 && status != "ready"; poll++)
        {
            await Task.Delay(100, Cancel);
            status = (await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/exports/{started.GetProperty("id").GetString()}"), Cancel)).GetProperty("status").GetString()!;
        }

        Assert.Equal("ready", status);
    }

    [Fact]
    public async Task Excel_FollowsTheFilter_AndHoldsTheLabels()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var answer = await ExportAsync(factory, owner, id, new { format = "excel", status = "complete", language = "en" });
        using var workbook = new ZipArchive(new MemoryStream(await answer.Content.ReadAsByteArrayAsync(Cancel)), ZipArchiveMode.Read);
        using var reader = new StreamReader(workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = await reader.ReadToEndAsync(Cancel);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("customer-satisfaction-2026.xlsx", answer.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(3, sheet.Split("<row ").Length - 1);
        Assert.Contains("<t>Search</t>", sheet, StringComparison.Ordinal);
        Assert.Contains("<t>Messe</t>", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spss_IsAZipWithTheDataAndTheSyntax()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var answer = await ExportAsync(factory, owner, id, new { format = "spss" });
        using var zip = new ZipArchive(new MemoryStream(await answer.Content.ReadAsByteArrayAsync(Cancel)), ZipArchiveMode.Read);

        Assert.Equal("customer-satisfaction-2026-spss.zip", answer.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(["customer-satisfaction-2026.csv", "customer-satisfaction-2026.sps"], zip.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public async Task Viewers_DoNotExport_AndUnknownFormatsAreRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        var unknown = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "sav" }, Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Viewer'");
        var viewer = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.Forbidden), (unknown.StatusCode, viewer.StatusCode));
        Assert.Equal(0L, await SurveysApiTests.ScalarAsync(factory, "SELECT count(*) FROM export"));
    }

    [Fact]
    public async Task Recent_ListsThisSurveysExports_NewestFirst()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        await ExportAsync(factory, owner, id, new { format = "csv" });
        await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "json" }, Cancel);

        var recent = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/surveys/{id}/exports"), Cancel);

        Assert.Equal([("json", "queued"), ("csv", "ready")], recent.GetProperty("items").EnumerateArray().Select(item => (item.GetProperty("format").GetString(), item.GetProperty("status").GetString())));
    }

    [Fact]
    public async Task AFourthRunningExport_IsRefused()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;

        for (var i = 0; i < 3; i++)
        {
            (await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel)).EnsureSuccessStatusCode();
        }

        var fourth = await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal("too-many-exports", (await JsonAsync(fourth)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AfterSevenDays_TheFileIsDeleted_AndTheExportSaysSo()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        var started = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel));
        var exportId = started.GetProperty("id").GetString();
        await RunJobsAsync(factory);

        await SurveysApiTests.ScalarAsync(factory, "UPDATE job SET run_at = now() WHERE kind = 'delete-export'");
        await RunJobsAsync(factory);
        var expired = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/exports/{exportId}"), Cancel);
        var file = await owner.GetAsync(Url($"/api/app/exports/{exportId}/file"), Cancel);

        Assert.Equal("expired", expired.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, expired.GetProperty("file").ValueKind);
        Assert.Equal(HttpStatusCode.Gone, file.StatusCode);
        Assert.Empty(Directory.EnumerateFiles(factory.DataDirectory, "*.csv", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AnExportWhoseJobGaveUp_IsFailed_AndItsFileIsNotReady()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        var started = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel));
        var exportId = started.GetProperty("id").GetString();

        await SurveysApiTests.ScalarAsync(factory, "UPDATE job SET attempts = 5, done_at = now(), error = 'disk full' WHERE kind = 'prepare-export'");
        var failed = await owner.GetFromJsonAsync<JsonElement>(Url($"/api/app/exports/{exportId}"), Cancel);
        var file = await owner.GetAsync(Url($"/api/app/exports/{exportId}/file"), Cancel);

        Assert.Equal(("failed", "export-failed"), (failed.GetProperty("status").GetString(), failed.GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, file.StatusCode);
    }

    [Fact]
    public async Task Exports_AreForTheOrganisationOnly()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        var (owner, id) = await ResultsApiTests.AnsweredAsync(factory);
        using var _ = owner;
        var started = await JsonAsync(await owner.PostAsJsonAsync(Url($"/api/app/surveys/{id}/exports"), new { format = "csv" }, Cancel));
        var exportId = started.GetProperty("id").GetString();
        await RunJobsAsync(factory);
        var other = Guid.CreateVersion7();
        await SurveysApiTests.ScalarAsync(factory, $"""
            INSERT INTO tenant (id, name, slug, created_at) VALUES ('{other}', 'Acme Schweiz AG', 'acme-ch', now());
            INSERT INTO membership (id, tenant_id, user_id, role, created_at) SELECT gen_random_uuid(), '{other}', id, 'Owner', now() FROM app_user;
            """);

        (await owner.PutAsJsonAsync(Url("/api/app/session/tenant"), new { tenantId = other }, Cancel)).EnsureSuccessStatusCode();
        var export = await owner.GetAsync(Url($"/api/app/exports/{exportId}"), Cancel);
        var file = await owner.GetAsync(Url($"/api/app/exports/{exportId}/file"), Cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (export.StatusCode, file.StatusCode));
    }
}
