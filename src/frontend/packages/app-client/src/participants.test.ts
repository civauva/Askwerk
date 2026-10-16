// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createParticipantsClient } from "./participants";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

describe("createParticipantsClient (AWK-7)", () => {
  it("checks a list, invites, lists, reminds and points to the links file", async () => {
    const requests: Request[] = [];
    const check = { participants: [{ email: "anna@acme.at", name: null, language: null }], duplicates: 0, alreadyInvited: 0, unsubscribed: 0, invalid: [] };
    const answers = [
      Response.json(check),
      Response.json(check),
      Response.json({ invited: 1, skipped: 0 }, { status: 202 }),
      Response.json({ items: [], total: 0, counts: {}, reminderAt: null }),
      new Response(null, { status: 202 }),
      new Response(null, { status: 202 }),
      Response.json({ removed: 1, kept: 1 }),
    ];
    const client = createParticipantsClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    await client.checkText("s1", "anna@acme.at");
    await client.checkFile("s1", new File(["email\nanna@acme.at"], "list.csv", { type: "text/csv" }));
    const invited = await client.invite("s1", { participants: check.participants, deadline: "2026-10-28", reminder: true, send: true });
    await client.list("s1", { sort: "-status", offset: 50, limit: 50 });
    await client.remind("s1");
    await client.remind("s1", { participantIds: ["p1", "p2"] });
    const removed = await client.remove("s1", { all: true });

    expect(invited.invited).toBe(1);
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "POST /api/app/surveys/s1/participants/check",
      "POST /api/app/surveys/s1/participants/check",
      "POST /api/app/surveys/s1/invitations",
      "GET /api/app/surveys/s1/participants",
      "POST /api/app/surveys/s1/participants/remind",
      "POST /api/app/surveys/s1/participants/remind",
      "POST /api/app/surveys/s1/participants/remove",
    ]);
    expect(new URL(requests[3]!.url).search).toBe("?sort=-status&offset=50&limit=50");
    expect(await requests[5]!.json()).toEqual({ participantIds: ["p1", "p2"] });
    expect(await requests[6]!.json()).toEqual({ all: true });
    expect(removed).toEqual({ removed: 1, kept: 1 });
    expect(requests[1]!.headers.get("Content-Type")).toMatch(/^multipart\/form-data/);
    expect(client.linksUrl("s1")).toBe("/api/app/surveys/s1/participants/links.csv");
  });
});
