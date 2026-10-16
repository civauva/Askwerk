// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createApiKeysClient } from "./apiKeys";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

describe("createApiKeysClient (AWK-25)", () => {
  it("lists, creates with the secret once, and revokes keys", async () => {
    const requests: Request[] = [];
    const key = { id: "k1", name: "Reporting server", scopes: ["surveys:read", "responses:read"], shown: "awk_7Hq2…", createdAt: "2026-10-15T08:00:00Z", lastUsedAt: null, expiresAt: null };
    const answers = [Response.json({ items: [key] }), Response.json({ key, secret: "awk_7Hq2" }, { status: 201 }), new Response(null, { status: 204 })];
    const client = createApiKeysClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    const listed = await client.list();
    const created = await client.create({ name: "Reporting server", scopes: ["surveys:read", "responses:read"], expiresInDays: 90 });
    await client.revoke("k 1");

    expect(listed).toEqual([key]);
    expect(created.secret).toBe("awk_7Hq2");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual(["GET /api/app/api-keys", "POST /api/app/api-keys", "DELETE /api/app/api-keys/k%201"]);
    expect(await requests[1]!.json()).toEqual({ name: "Reporting server", scopes: ["surveys:read", "responses:read"], expiresInDays: 90 });
  });
});
