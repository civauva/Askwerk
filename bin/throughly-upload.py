#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Uploads images to Throughly work items through the same MCP server (add_task_mockup), reading the
# files from disk so the image bytes never pass through the chat. Token from THROUGHLY_TOKEN.
# Usage: bin/throughly-upload.py <manifest.json>   manifest: [{"taskId", "file", "caption"}, ...]
import base64, json, os, sys, urllib.request

URL = "https://my.throughly.app/mcp"
TOKEN = os.environ["THROUGHLY_TOKEN"]
session = None
counter = 0


def rpc(method, params=None, notify=False):
    global session, counter
    body = {"jsonrpc": "2.0", "method": method}
    if params is not None:
        body["params"] = params
    if not notify:
        counter += 1
        body["id"] = counter
    headers = {"Authorization": f"Bearer {TOKEN}", "Content-Type": "application/json",
               "Accept": "application/json, text/event-stream"}
    if session:
        headers["Mcp-Session-Id"] = session
    req = urllib.request.Request(URL, data=json.dumps(body).encode(), headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=120) as resp:
        session = resp.headers.get("Mcp-Session-Id") or session
        raw = resp.read().decode()
    if notify or not raw.strip():
        return None
    if raw.lstrip().startswith("{"):
        return json.loads(raw)
    # Server-sent events: take the last data line that holds JSON.
    data = [line[5:].strip() for line in raw.splitlines() if line.startswith("data:")]
    return json.loads(data[-1])


rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "askwerk-upload", "version": "1"}})
rpc("notifications/initialized", notify=True)

for item in json.load(open(sys.argv[1], encoding="utf-8")):
    data = base64.b64encode(open(item["file"], "rb").read()).decode()
    result = rpc("tools/call", {"name": "add_task_mockup", "arguments": {
        "taskId": item["taskId"], "fileName": os.path.basename(item["file"]), "imageBase64": data, "caption": item["caption"]}})
    content = result.get("result", {}).get("content", [{}])
    is_error = result.get("result", {}).get("isError") or "error" in result
    print(("ERROR " if is_error else "ok    ") + os.path.basename(item["file"]) + " -> " + (content[0].get("text", "")[:160] if content else json.dumps(result)[:160]))
