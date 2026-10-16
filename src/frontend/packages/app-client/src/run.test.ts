// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createPreviewClient, createRunClient, surveyIdOf, unsubscribe, unsubscribeInfo } from "./run";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

function fakeFetch(answers: Response[]) {
  const requests: Request[] = [];
  const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
    return answers.shift()!;
  };

  return { requests, fetch };
}

describe("createRunClient", () => {
  it("opens, starts, saves pages and completes", async () => {
    const { requests, fetch } = fakeFetch([
      Response.json({ survey: { id: "s1", organisation: "Acme GmbH", state: "open", definition: {} }, response: null }),
      Response.json({ status: "partial", pageId: "p1", language: "de", answers: {} }, { status: 201 }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
    ]);
    const client = createRunClient({ fetch, telemetry });

    const opened = await client.open("s1");
    const started = await client.start("s1", "de");
    await client.savePage("s1", "p1", { q1: { number: 4 } });
    await client.complete("s1");

    expect(opened?.survey.organisation).toBe("Acme GmbH");
    expect(started.pageId).toBe("p1");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/run/s1",
      "POST /api/app/run/s1/response",
      "PUT /api/app/run/s1/response/pages/p1",
      "POST /api/app/run/s1/response/complete",
    ]);
    expect(await requests[2]?.json()).toEqual({ answers: { q1: { number: 4 } } });
    expect([client.assetUrl("s1", "a1"), client.assetUrl("s1", "a1", 960)]).toEqual(["/api/app/run/s1/assets/a1", "/api/app/run/s1/assets/a1?w=960"]);
  });

  it("answers null for a survey that is not published", async () => {
    const { fetch } = fakeFetch([Response.json({ status: 404 }, { status: 404 })]);

    expect(await createRunClient({ fetch, telemetry }).open("nope")).toBeNull();
  });
});

describe("createPreviewClient", () => {
  it("opens the draft, checks pages without storing, and starts and completes without the server", async () => {
    const { requests, fetch } = fakeFetch([
      Response.json({ survey: { id: "s1", organisation: "Acme GmbH", state: "open", definition: { pages: [{ id: "p1" }, { id: "p2" }] } }, response: null }),
      new Response(null, { status: 204 }),
    ]);
    const client = createPreviewClient({ fetch, telemetry });

    const opened = await client.open("s1");
    const started = await client.start("s1", "en");
    await client.savePage("s1", "p1", { q1: { number: 4 } });
    await client.complete("s1");

    expect(opened?.survey.organisation).toBe("Acme GmbH");
    expect(started).toEqual({ status: "partial", pageId: "p1", language: "en", answers: {} });
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/surveys/s1/preview",
      "PUT /api/app/surveys/s1/preview/pages/p1",
    ]);
    expect(await requests[1]?.json()).toEqual({ answers: { q1: { number: 4 } } });
    expect(client.assetUrl("s1", "a1", 480)).toBe("/api/app/assets/a1?w=480");
  });

  it("answers null to anyone who may not see the draft, like a link that leads nowhere", async () => {
    for (const status of [401, 403, 404]) {
      const { fetch } = fakeFetch([Response.json({ status }, { status })]);

      expect(await createPreviewClient({ fetch, telemetry }).open("s1")).toBeNull();
    }
  });
});

describe("surveyIdOf (AWK-7, short links)", () => {
  it("keeps an id, finds the survey of a code, and keeps an unknown code so the runner says it is not found", async () => {
    const asked: string[] = [];
    const fetch = async (input: RequestInfo | URL) => {
      asked.push(String(input));
      return String(input).endsWith("/kz26b7mq") ? Response.json({ surveyId: "01a13b8f-0000-7000-8000-000000000001" }) : new Response(null, { status: 404 });
    };

    expect(await surveyIdOf({ fetch, telemetry }, "01a13b8f-0000-7000-8000-000000000009")).toBe("01a13b8f-0000-7000-8000-000000000009");
    expect(await surveyIdOf({ fetch, telemetry }, "kz26b7mq")).toBe("01a13b8f-0000-7000-8000-000000000001");
    expect(await surveyIdOf({ fetch, telemetry }, "unknown1")).toBe("unknown1");
    expect(asked).toEqual(["/api/app/run/code/kz26b7mq", "/api/app/run/code/unknown1"]);
  });
});

describe("personal links (AWK-7)", () => {
  it("opens with the token, starts the response with it, and reads and answers an unsubscribe link", async () => {
    const requests: Request[] = [];
    const answers = [
      Response.json({ survey: null, response: null }),
      Response.json({ status: "partial", pageId: "p1", language: "de", answers: {} }, { status: 201 }),
      Response.json({ organisation: "Acme GmbH", email: "a***@acme.at", language: "de", done: false }),
      new Response(null, { status: 204 }),
    ];
    const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
      requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
      return answers.shift()!;
    };
    const client = createRunClient({ fetch, telemetry }, "tok123");

    await client.open("s1");
    await client.start("s1", "de");
    const page = await unsubscribeInfo({ fetch, telemetry }, "tok123");
    await unsubscribe({ fetch, telemetry }, "tok123");

    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}${new URL(request.url).search}`)).toEqual([
      "GET /api/app/run/s1?t=tok123",
      "POST /api/app/run/s1/response",
      "GET /api/app/run/unsubscribe/tok123",
      "POST /api/app/run/unsubscribe/tok123",
    ]);
    expect(await requests[1]!.json()).toEqual({ language: "de", token: "tok123" });
    expect(page.email).toBe("a***@acme.at");
  });
});
