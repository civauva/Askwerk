// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createDomainsClient } from "./domains";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

describe("createDomainsClient (AWK-7)", () => {
  it("lists, adds, verifies, chooses the primary domain and removes", async () => {
    const requests: Request[] = [];
    const domain = { id: "d1", hostname: "umfragen.acme.at", status: "pending" };
    const answers = [
      Response.json({ system: { hostname: "surveys.acme.at", isPrimary: true }, items: [] }),
      Response.json(domain, { status: 201 }),
      Response.json({ ...domain, status: "verified" }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
      Response.json({ origins: ["https://www.acme.at"] }),
      new Response(null, { status: 204 }),
    ];
    const client = createDomainsClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    const list = await client.list();
    await client.add("umfragen.acme.at");
    const verified = await client.verify("d1");
    await client.setPrimary(null);
    await client.remove("d1");
    const origins = await client.embedding();
    await client.setEmbedding(["https://www.acme.at"]);

    expect(list.system.hostname).toBe("surveys.acme.at");
    expect(verified.status).toBe("verified");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/domains",
      "POST /api/app/domains",
      "POST /api/app/domains/d1/verify",
      "PUT /api/app/domains/primary",
      "DELETE /api/app/domains/d1",
      "GET /api/app/embedding",
      "PUT /api/app/embedding",
    ]);
    expect(origins).toEqual(["https://www.acme.at"]);
    expect(await requests[3]!.json()).toEqual({ domainId: null });
  });
});
