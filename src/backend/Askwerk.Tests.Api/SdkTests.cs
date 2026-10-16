// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Net.Http.Json;
using WebhookService = Askwerk.Api.Integrations.WebhookService;
using Askwerk.Sdk;
using Askwerk.Tests.Integration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Askwerk.Tests.Api;

/// <summary>
/// The three SDKs (src/sdks, AWK-26) against a running Askwerk on a real port, nothing mocked: the same scenario in
/// C#, TypeScript and Python. Needs Node and Python, so it runs in the full run only (ASKWERK_TEST_SDKS=1); whether the
/// generated code is current is checked in every run.
/// </summary>
public sealed class SdkTests(PostgresServer server)
{
    private static readonly string[] Scopes = ["surveys:read", "responses:read", "webhooks:manage"];
    private static readonly string[] ReadOnly = ["surveys:read"];
    private static readonly string[] Ads = ["2"];

    public static bool Enabled => Environment.GetEnvironmentVariable("ASKWERK_TEST_SDKS") == "1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task TheGeneratedCode_IsCurrent()
    {
        var (exitCode, output) = await RunAsync("node", ["src/sdks/generate.mjs", "--check"], []);

        Assert.True(exitCode == 0, output);
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "The SDKs run in the full run only (ASKWERK_TEST_SDKS=1).")]
    public async Task EverySdk_RunsTheSameScenario_AgainstARealAskwerk()
    {
        await using var setup = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(setup);
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();
        using var respondent = await setup.CreateReadyClientAsync();
        (await respondent.PostAsJsonAsync(Url($"/api/app/run/{id}/response"), new { language = "en" }, Cancel)).EnsureSuccessStatusCode();
        (await respondent.PutAsJsonAsync(Url($"/api/app/run/{id}/response/pages/p1"), new { answers = new { q1 = new { number = 4 }, q2 = new { options = Ads } } }, Cancel)).EnsureSuccessStatusCode();
        var (key, _) = await ApiKeysApiTests.CreateKeyAsync(owner, Scopes);
        var (clientId, clientSecret, _) = await OAuthApiTests.CreateClientAsync(owner, ReadOnly);

        // The same database, served on a real port, with jobs running by themselves as in production.
        await using var running = new AskwerkApiFactory(setup.OwnerConnectionString, setup.DataDirectory) { RunJobsByThemselves = true };
        running.UseKestrel(0);
        running.StartServer();
        using (var probe = running.CreateClient())
        {
            await running.WaitUntilReadyAsync(probe);
        }
        var address = running.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string Body = """{"type":"survey.published"}""";
        var environment = new Dictionary<string, string>
        {
            ["ASKWERK_URL"] = address,
            ["ASKWERK_KEY"] = key,
            ["ASKWERK_CLIENT_ID"] = clientId,
            ["ASKWERK_CLIENT_SECRET"] = clientSecret,
            ["ASKWERK_SURVEY"] = id,
            ["ASKWERK_SIGNATURE"] = WebhookService.Signature("whsec_test", timestamp, Body),
            ["ASKWERK_SIGNED_BODY"] = Body,
        };

        await CSharpScenarioAsync(environment);
        var typescript = await RunAsync("node", ["src/sdks/typescript/tests/scenario.ts"], environment);
        var python = await RunAsync("python", ["-I", "src/sdks/python/tests/scenario.py"], environment);

        Assert.True(typescript.ExitCode == 0, "TypeScript: " + typescript.Output);
        Assert.True(python.ExitCode == 0, "Python: " + python.Output);
    }

    private static async Task CSharpScenarioAsync(Dictionary<string, string> environment)
    {
        var baseUrl = new Uri(environment["ASKWERK_URL"]);
        var survey = Guid.Parse(environment["ASKWERK_SURVEY"]);
        using var askwerk = new AskwerkClient(baseUrl, AskwerkAuth.ApiKey(environment["ASKWERK_KEY"]));

        var published = new List<Guid>();
        await foreach (var item in askwerk.Surveys.ListAllAsync(status: "published", cancellationToken: Cancel))
        {
            published.Add(item.Id);
        }

        Assert.Equal([survey], published);
        Assert.Equal("Customer satisfaction 2026", (await askwerk.Surveys.GetAsync(survey, Cancel)).Title);
        Assert.Contains((await askwerk.Responses.CodebookAsync(survey, "de", Cancel)).Variables, variable => variable.Variable == "BEFORE");

        var responses = new List<Sdk.PublicResponse>();
        await foreach (var response in askwerk.Responses.ListAllAsync(survey, cancellationToken: Cancel))
        {
            responses.Add(response);
        }

        Assert.Contains(Assert.Single(responses).Answers, answer => answer.Variable == "SAT" && answer.Value == "4");

        var export = await askwerk.Exports.StartAsync(survey, new PublicExportRequest { Format = "csv" }, cancellationToken: Cancel);
        var ready = await askwerk.Exports.WaitUntilReadyAsync(export.Id, TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(1), Cancel);
        Assert.NotEmpty(await askwerk.Exports.DownloadAsync(ready.Id, Cancel));

        var webhook = await askwerk.Webhooks.CreateAsync(new Sdk.WebhookRequest { Url = "https://hooks.example.org/askwerk", Events = ["survey.published"] }, Cancel);
        Assert.Contains((await askwerk.Webhooks.ListAsync(Cancel)).Items, each => each.Id == webhook.Webhook.Id);
        await askwerk.Webhooks.DeleteAsync(webhook.Webhook.Id, Cancel);

        var missing = await Assert.ThrowsAsync<AskwerkException>(() => askwerk.Surveys.GetAsync(Guid.NewGuid(), Cancel));
        Assert.Equal((404, "https://askwerk.org/problems/survey-not-found"), (missing.Status, missing.Type));

        using var client = new AskwerkClient(baseUrl, AskwerkAuth.ClientCredentials(environment["ASKWERK_CLIENT_ID"], environment["ASKWERK_CLIENT_SECRET"]));
        Assert.Single((await client.Surveys.ListAsync(cancellationToken: Cancel)).Items);

        Assert.True(AskwerkWebhooks.Verify("whsec_test", environment["ASKWERK_SIGNATURE"], environment["ASKWERK_SIGNED_BODY"]));
        Assert.False(AskwerkWebhooks.Verify("whsec_test", environment["ASKWERK_SIGNATURE"], environment["ASKWERK_SIGNED_BODY"] + " "));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string program, string[] arguments, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(program) { WorkingDirectory = Snapshot.FindRepositoryRoot(), RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(Cancel);
        var error = process.StandardError.ReadToEndAsync(Cancel);
        await process.WaitForExitAsync(Cancel);

        return (process.ExitCode, await output + await error);
    }
}
