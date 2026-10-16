// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createThemesClient } from "./themes";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

function fakeFetch(answers: Response[]) {
  const requests: Request[] = [];
  const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
    return answers.shift()!;
  };

  return { requests, fetch };
}

describe("createThemesClient", () => {
  it("lists, creates, changes, makes default and deletes themes", async () => {
    const { requests, fetch } = fakeFetch([
      Response.json({ items: [{ id: "t1", name: "Acme", isDefault: true, tokens: {} }] }),
      Response.json({ id: "t2" }, { status: 201 }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
    ]);
    const client = createThemesClient({ fetch, telemetry });

    const themes = await client.list();
    const created = await client.create("Event", { schemaVersion: 1 });
    await client.update("t2", "Event 2026", { schemaVersion: 1 });
    await client.makeDefault("t2");
    await client.remove("t1");

    expect(themes[0]?.name).toBe("Acme");
    expect(created).toBe("t2");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/themes",
      "POST /api/app/themes",
      "PUT /api/app/themes/t2",
      "POST /api/app/themes/t2/default",
      "DELETE /api/app/themes/t1",
    ]);
    expect(await requests[2]?.json()).toEqual({ name: "Event 2026", tokens: { schemaVersion: 1 } });
  });

  it("uploads a file as form data and names the address of a stored file", async () => {
    const { requests, fetch } = fakeFetch([Response.json({ id: "a1", kind: "image", contentType: "image/png", size: 3, name: "logo.png" }, { status: 201 })]);
    const client = createThemesClient({ fetch, telemetry });

    const stored = await client.upload(new File([new Uint8Array([1, 2, 3])], "logo.png", { type: "image/png" }), "image");

    expect(stored.id).toBe("a1");
    expect(requests[0]?.headers.get("content-type")).toMatch(/^multipart\/form-data; boundary=/);
    const form = await requests[0]!.formData();
    expect([form.get("kind"), (form.get("file") as File).name]).toEqual(["image", "logo.png"]);
    expect(client.assetUrl("a1")).toBe("/api/app/assets/a1");
  });
});
