// SPDX-License-Identifier: Apache-2.0
// The job: reads appsettings.json (or the environment variables ASKWERK_API_KEY and SQLSERVER_CONNECTION_STRING for
// the secrets), copies every configured survey, logs a line per survey and exits 0, or 1 when a survey failed.
using System.Net.Http.Headers;
using System.Text.Json;
using AskwerkSqlSync;

var file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
using var settings = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
var askwerk = settings.RootElement.GetProperty("Askwerk");

string Setting(string environment, JsonElement section, string name) =>
    Environment.GetEnvironmentVariable(environment) is { Length: > 0 } value ? value : section.GetProperty(name).GetString() ?? "";

var baseUrl = Setting("ASKWERK_BASE_URL", askwerk, "BaseUrl").TrimEnd('/') + "/";
var apiKey = Setting("ASKWERK_API_KEY", askwerk, "ApiKey");
var language = askwerk.TryGetProperty("Language", out var chosen) ? chosen.GetString() : null;
var connectionString = Setting("SQLSERVER_CONNECTION_STRING", settings.RootElement.GetProperty("SqlServer"), "ConnectionString");

using var api = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(2) };
api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
var sync = new Sync(api, connectionString, string.IsNullOrWhiteSpace(language) ? null : language);

var configured = askwerk.TryGetProperty("Surveys", out var list) ? list.EnumerateArray().Select(survey => Guid.Parse(survey.GetString()!)).ToList() : [];
var surveys = configured.Count > 0 ? configured : await sync.PublishedSurveysAsync(CancellationToken.None);
var failed = 0;

foreach (var survey in surveys)
{
    try
    {
        var copied = await sync.RunAsync(survey, CancellationToken.None);
        Console.WriteLine($"{DateTimeOffset.UtcNow:u} survey {survey}: {copied} new or changed responses");
    }
    catch (Exception error) when (error is HttpRequestException or Microsoft.Data.SqlClient.SqlException or JsonException)
    {
        failed++;
        Console.Error.WriteLine($"{DateTimeOffset.UtcNow:u} survey {survey}: failed: {error.Message}");
    }
}

return failed == 0 ? 0 : 1;
