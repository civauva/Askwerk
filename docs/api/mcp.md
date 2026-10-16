# MCP server

Askwerk speaks the Model Context Protocol at `/mcp`, so AI assistants can work with an organisation's surveys. It uses Streamable HTTP: one JSON-RPC 2.0 message per `POST /mcp`, answered with JSON. Authenticate with an API key or an OAuth access token, exactly as for the Public API; each tool needs the scope it names and can never do more than the Public API with the same scopes.

## Tools

| Tool | Scope | Arguments |
| --- | --- | --- |
| `list_surveys` | `surveys:read` | `status`? (draft, published, closed), `cursor`? |
| `get_survey` | `surveys:read` | `surveyId` |
| `start_export` | `responses:read` | `surveyId`, `format` (excel, csv, spss, stata, json), `language`? |
| `get_export` | `responses:read` | `exportId` |

Each tool answers with the same JSON as its Public API endpoint, as text. A refusal (missing scope, unknown survey) comes back as a result with `isError: true` and the problem's code. Methods: `initialize`, `ping`, `tools/list`, `tools/call`; notifications are accepted with 202.

## Connect

Claude Desktop (`claude_desktop_config.json`), through a small bridge for remote servers:

```json
{
  "mcpServers": {
    "askwerk": {
      "command": "npx",
      "args": ["mcp-remote", "https://surveys.acme.at/mcp", "--header", "Authorization: Bearer ${ASKWERK_KEY}"],
      "env": { "ASKWERK_KEY": "awk_…" }
    }
  }
}
```

VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "askwerk": { "type": "http", "url": "https://surveys.acme.at/mcp", "headers": { "Authorization": "Bearer ${input:askwerkKey}" } }
  },
  "inputs": [{ "id": "askwerkKey", "type": "promptString", "description": "Askwerk API key", "password": true }]
}
```

With curl:

```bash
curl https://surveys.acme.at/mcp -H "Authorization: Bearer $ASKWERK_KEY" -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_surveys","arguments":{"status":"published"}}}'
```

Use a key with only the scopes the assistant needs, e.g. Read results. Every tool call is in the audit log like the endpoint it uses.
