// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Askwerk.Api.Identity;
using Askwerk.Application.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Developers;

/// <summary>
/// The Developers hub (AWK-34, W27): this Askwerk's address for the public API, a Postman collection with it built
/// in, and the example project as a download. Every signed-in member reads them; keys stay with owners and admins.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/developers")]
public sealed class DevelopersController(IInstanceSettingsRepository settings) : ControllerBase
{
    private const string OpenApiResource = "Askwerk.Api.Developers.openapi.json";
    private const string ExamplePrefix = "Askwerk.Api.Developers.SqlServerSync.";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The addresses the public API answers at.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The base URL and the API's root.</returns>
    [HttpGet]
    [ProducesResponseType<DevelopersResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DevelopersResponse>> Get(CancellationToken cancellationToken)
    {
        var baseUrl = await BaseUrlAsync(cancellationToken);

        return new DevelopersResponse(baseUrl, $"{baseUrl}/api/v1");
    }

    /// <summary>
    /// A Postman collection (v2.1) of every public endpoint, one folder per area, with baseUrl set to this Askwerk and
    /// an empty apiKey variable for the bearer key.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The collection as a file.</returns>
    [HttpGet("postman")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Postman(CancellationToken cancellationToken)
    {
        var baseUrl = await BaseUrlAsync(cancellationToken);
        var collection = PostmanExport.From(OpenApi(), baseUrl);
        var host = new Uri(baseUrl).Host;

        return File(JsonSerializer.SerializeToUtf8Bytes(collection, Indented), "application/json", $"askwerk-{host}.postman_collection.json");
    }

    /// <summary>The example project that syncs responses to SQL Server, as a zip (docs/integrations/sql-server-sync).</summary>
    /// <returns>The zip file.</returns>
    [HttpGet("examples/sql-server-sync")]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult SqlServerSync()
    {
        var assembly = typeof(DevelopersController).Assembly;
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ExamplePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry($"askwerk-sql-server-sync/{name[ExamplePrefix.Length..]}", CompressionLevel.Optimal);
                using var target = entry.Open();
                using var source = assembly.GetManifestResourceStream(name)!;
                source.CopyTo(target);
            }
        }

        return File(buffer.ToArray(), "application/zip", "askwerk-sql-server-sync.zip");
    }

    /// <summary>The public API's OpenAPI description as committed (docs/api/openapi.json).</summary>
    /// <returns>The document.</returns>
    public static JsonNode OpenApi()
    {
        using var stream = typeof(DevelopersController).Assembly.GetManifestResourceStream(OpenApiResource)
            ?? throw new InvalidOperationException("The OpenAPI description is not embedded.");

        return JsonNode.Parse(stream)!;
    }

    private async Task<string> BaseUrlAsync(CancellationToken cancellationToken) =>
        (await settings.FindAsync(cancellationToken))?.BaseAddress.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
}

/// <summary>Where the public API answers.</summary>
/// <param name="BaseUrl">This Askwerk's address.</param>
/// <param name="ApiUrl">The public API's root.</param>
public sealed record DevelopersResponse(string BaseUrl, string ApiUrl);

/// <summary>Builds a Postman collection (v2.1) from an OpenAPI description (AWK-34).</summary>
public static class PostmanExport
{
    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete"];

    /// <summary>The collection: a folder per area, a request per operation, baseUrl and apiKey as variables.</summary>
    /// <param name="openApi">The OpenAPI description.</param>
    /// <param name="baseUrl">This Askwerk's address.</param>
    /// <returns>The collection as JSON.</returns>
    public static JsonObject From(JsonNode openApi, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(openApi);
        var folders = new Dictionary<string, JsonArray>(StringComparer.Ordinal);

        foreach (var (path, operations) in openApi["paths"]!.AsObject())
        {
            foreach (var method in Methods)
            {
                if (operations?[method] is not JsonObject operation)
                {
                    continue;
                }

                var area = AreaOf(operation["tags"]?[0]?.GetValue<string>() ?? "API");

                if (!folders.TryGetValue(area, out var items))
                {
                    folders[area] = items = [];
                }

                items.Add(Request(path, method, operation));
            }
        }

        return new JsonObject
        {
            ["info"] = new JsonObject
            {
                ["name"] = $"Askwerk API · {new Uri(baseUrl).Host}",
                ["description"] = "The Askwerk public API. Set apiKey to a key from Organisation → API, or a trial key from Developers.",
                ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
            },
            ["auth"] = new JsonObject { ["type"] = "bearer", ["bearer"] = new JsonArray(new JsonObject { ["key"] = "token", ["value"] = "{{apiKey}}", ["type"] = "string" }) },
            ["variable"] = new JsonArray(
                new JsonObject { ["key"] = "baseUrl", ["value"] = baseUrl, ["type"] = "string" },
                new JsonObject { ["key"] = "apiKey", ["value"] = "", ["type"] = "secret" }),
            ["item"] = new JsonArray([.. folders.Select(folder => (JsonNode)new JsonObject { ["name"] = folder.Key, ["item"] = folder.Value })]),
        };
    }

    /// <summary>"PublicResponses" → "Responses".</summary>
    /// <param name="tag">The OpenAPI tag.</param>
    /// <returns>The area's name.</returns>
    public static string AreaOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var name = tag.StartsWith("Public", StringComparison.Ordinal) && tag.Length > "Public".Length ? tag["Public".Length..] : tag;

        return name.EndsWith("Controller", StringComparison.Ordinal) ? name[..^"Controller".Length] : name;
    }

    private static JsonObject Request(string path, string method, JsonObject operation)
    {
        var parameters = operation["parameters"]?.AsArray() ?? [];
        var query = new JsonArray([.. parameters.Where(parameter => parameter?["in"]?.GetValue<string>() == "query").Select(parameter => (JsonNode)new JsonObject
        {
            ["key"] = parameter!["name"]!.GetValue<string>(),
            ["value"] = "",
            ["description"] = parameter["description"]?.GetValue<string>() ?? "",
            ["disabled"] = parameter["required"]?.GetValue<bool>() != true,
        })]);
        var variables = new JsonArray([.. parameters.Where(parameter => parameter?["in"]?.GetValue<string>() == "path").Select(parameter => (JsonNode)new JsonObject
        {
            ["key"] = parameter!["name"]!.GetValue<string>(),
            ["value"] = "",
            ["description"] = parameter["description"]?.GetValue<string>() ?? "",
        })]);
        var segments = path.Trim('/').Split('/').Select(segment => segment.StartsWith('{') ? $":{segment.Trim('{', '}')}" : segment).ToList();
        var request = new JsonObject
        {
            ["method"] = method.ToUpperInvariant(),
            ["header"] = new JsonArray(),
            ["url"] = new JsonObject
            {
                ["raw"] = "{{baseUrl}}/" + string.Join('/', segments),
                ["host"] = new JsonArray("{{baseUrl}}"),
                ["path"] = new JsonArray([.. segments.Select(segment => (JsonNode)segment)]),
                ["query"] = query,
                ["variable"] = variables,
            },
            ["description"] = operation["description"]?.GetValue<string>() ?? "",
        };

        if (operation["requestBody"] is not null)
        {
            request["body"] = new JsonObject { ["mode"] = "raw", ["raw"] = "{\n}", ["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = "json" } } };
            request["header"]!.AsArray().Add(new JsonObject { ["key"] = "Content-Type", ["value"] = "application/json" });
        }

        // The token endpoint takes the client's credentials, not a bearer key.
        if (path.EndsWith("/oauth/token", StringComparison.Ordinal))
        {
            request["auth"] = new JsonObject { ["type"] = "noauth" };
        }

        return new JsonObject { ["name"] = operation["summary"]?.GetValue<string>() ?? $"{method.ToUpperInvariant()} {path}", ["request"] = request };
    }
}
