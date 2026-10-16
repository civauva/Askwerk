// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Nodes;
using Askwerk.Api.App.Exports;
using Askwerk.Api.App.Surveys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Askwerk.Api.Integrations;

/// <summary>
/// The MCP server (AWK-32, C32 C): Model Context Protocol over Streamable HTTP, one JSON-RPC 2.0 message per POST to
/// /mcp, authenticated like the Public API. Its tools call the Public API's own actions with the caller's scopes, so
/// it never does more than the Public API (docs/api/mcp.md). Methods: initialize, ping, tools/list, tools/call.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Policy = PublicApi.Integration)]
[Route("mcp")]
public sealed class McpController(IServiceProvider services, IConfiguration configuration) : ControllerBase
{
    private const string Protocol = "2025-06-18";
    private static readonly string[] Protocols = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private static readonly Tool[] Tools =
    [
        new("list_surveys", "Lists the organisation's surveys, newest change first, with their status and number of responses.", "surveys:read",
            Schema(("status", "string", "draft, published or closed; all when left out", false), ("cursor", "string", "nextCursor of the page before", false))),
        new("get_survey", "Reads one survey: its versions, languages, number of responses and links.", "surveys:read",
            Schema(("surveyId", "string", "The survey's id", true))),
        new("start_export", "Starts an export of a survey's responses; poll it with get_export until it is ready.", "responses:read",
            Schema(("surveyId", "string", "The survey's id", true), ("format", "string", "excel, csv, spss, stata or json", true), ("language", "string", "Labels in this language, e.g. de or en", false))),
        new("get_export", "Reads an export: status, progress and, once ready, its file's download link.", "responses:read",
            Schema(("exportId", "string", "The export's id", true))),
    ];

    /// <summary>Handles one JSON-RPC message.</summary>
    /// <param name="message">The request or notification.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The JSON-RPC response; 202 for a notification.</returns>
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> Post(JsonElement message, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String)
        {
            return Ok(Error(null, -32600, "Invalid request: send one JSON-RPC 2.0 object with a method."));
        }

        var id = message.TryGetProperty("id", out var given) ? given.Clone() : (JsonElement?)null;
        var parameters = message.TryGetProperty("params", out var found) && found.ValueKind == JsonValueKind.Object ? found : default;

        if (id is null)
        {
            // Notifications (e.g. notifications/initialized) need no answer.
            return Accepted();
        }

        return Ok(methodValue.GetString() switch
        {
            "initialize" => Result(id, Initialize(parameters)),
            "ping" => Result(id, new JsonObject()),
            "tools/list" => Result(id, new JsonObject { ["tools"] = new JsonArray([.. Tools.Select(tool => (JsonNode)tool.Describe())]) }),
            "tools/call" => await CallAsync(id, parameters, cancellationToken),
            _ => Error(id, -32601, $"Method not found: {methodValue.GetString()}."),
        });
    }

    /// <summary>Streamable HTTP: this server sends no messages of its own, so there is no stream to open.</summary>
    /// <returns>405.</returns>
    [HttpGet]
    public IActionResult Get() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    private JsonObject Initialize(JsonElement parameters)
    {
        var asked = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("protocolVersion", out var version) ? version.GetString() : null;

        return new JsonObject
        {
            ["protocolVersion"] = asked is not null && Protocols.Contains(asked) ? asked : Protocol,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "askwerk", ["version"] = configuration["ASKWERK_VERSION"] ?? "0.0.0" },
            ["instructions"] = "Askwerk surveys of one organisation: list and read surveys, and export their responses. What a tool may do depends on the key's scopes.",
        };
    }

    private async Task<JsonObject> CallAsync(JsonElement? id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var name = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var arguments = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var given) && given.ValueKind == JsonValueKind.Object ? given : default;

        if (Tools.FirstOrDefault(tool => tool.Name == name) is not { } tool)
        {
            return Error(id, -32602, $"Unknown tool: {name}.");
        }

        if (!User.HasClaim(PublicApi.ScopeClaim, tool.Scope))
        {
            return Result(id, ToolResult(true, $"scope-missing: this tool needs the scope {tool.Scope}, which the key or client does not have."));
        }

        string? Text(string property) => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        Guid? IdOf(string property) => Guid.TryParse(Text(property), out var parsed) ? parsed : null;

        object? answer = name switch
        {
            "list_surveys" => await Controller<PublicSurveysController>().List(Text("status"), null, Text("cursor"), cancellationToken),
            "get_survey" => IdOf("surveyId") is { } surveyId ? await Controller<PublicSurveysController>().Get(surveyId, cancellationToken) : null,
            "start_export" => IdOf("surveyId") is { } exportSurvey
                ? await Controller<PublicExportsController>().Start(exportSurvey, new PublicExportRequest(Text("format"), Language: Text("language")), null, cancellationToken)
                : null,
            "get_export" => IdOf("exportId") is { } exportId ? await Controller<PublicExportsController>().Get(exportId, cancellationToken) : null,
            _ => null,
        };

        return Result(id, answer is null ? ToolResult(true, "invalid-arguments: give the ids as strings, as the tool's input schema says.") : Outcome(answer));
    }

    private T Controller<T>()
        where T : ControllerBase
    {
        var controller = ActivatorUtilities.CreateInstance<T>(services);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContext };

        return controller;
    }

    /// <summary>What an action answered, as a tool result: its JSON, or the problem as an error.</summary>
    private static JsonObject Outcome(object answer)
    {
        var value = answer switch
        {
            IConvertToActionResult convertible => convertible.Convert(),
            _ => answer,
        };

        return value switch
        {
            PublicApi.ProblemResult problem => ToolResult(true, $"{problem.Code}: {problem.Status}"),
            ObjectResult { StatusCode: >= 400 } failed => ToolResult(true, JsonSerializer.Serialize(failed.Value, JsonSerializerOptions.Web)),
            ObjectResult result => ToolResult(false, JsonSerializer.Serialize(result.Value, JsonSerializerOptions.Web)),
            _ => ToolResult(true, "unexpected-answer"),
        };
    }

    private static JsonObject ToolResult(bool isError, string text) =>
        new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError };

    private static JsonObject Result(JsonElement? id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id is { } known ? JsonNode.Parse(known.GetRawText()) : null, ["result"] = result };

    private static JsonObject Error(JsonElement? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id is { } known ? JsonNode.Parse(known.GetRawText()) : null, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] properties) =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(properties.Select(property => KeyValuePair.Create(property.Name, (JsonNode?)new JsonObject { ["type"] = property.Type, ["description"] = property.Description }))),
            ["required"] = new JsonArray([.. properties.Where(property => property.Required).Select(property => (JsonNode)property.Name)]),
        };

    private sealed record Tool(string Name, string Description, string Scope, JsonObject InputSchema)
    {
        public JsonObject Describe() =>
            new() { ["name"] = Name, ["description"] = $"{Description} Needs the scope {Scope}.", ["inputSchema"] = InputSchema.DeepClone() };
    }
}
