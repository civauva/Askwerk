// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createResultsClient } from "./results";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

describe("createResultsClient (AWK-24)", () => {
  it("asks for the overview, a page of responses and one response, with the filter in the query", async () => {
    const urls: string[] = [];
    const answers = [
      Response.json({ title: "Survey", languages: ["de"], defaultLanguage: "de", published: true, totals: { responses: 0, complete: 0, partial: 0, medianSeconds: null, lastAt: null }, versions: [1], questions: [] }),
      Response.json({ total: 0, columns: [], items: [], next: null }),
      Response.json({ id: "r1", status: "complete", version: 1, language: "de", startedAt: "2026-10-14T08:00:00Z", completedAt: null, durationSeconds: null, answers: [] }),
    ];
    const client = createResultsClient({
      fetch: async (input) => {
        urls.push(String(input));
        return answers.shift()!;
      },
      telemetry,
    });

    const overview = await client.overview("s 1", { status: "complete", version: 2, from: "2026-10-01T00:00:00Z" }, "en");
    await client.responses("s 1", { status: "partial", search: " Messe ", cursor: "abc", limit: 50, order: "oldest" }, "de");
    const response = await client.response("s 1", "r1");

    expect(overview.title).toBe("Survey");
    expect(response.id).toBe("r1");
    expect(urls).toEqual([
      "/api/app/surveys/s%201/results?status=complete&from=2026-10-01T00%3A00%3A00Z&version=2&language=en",
      "/api/app/surveys/s%201/responses?status=partial&search=Messe&cursor=abc&limit=50&order=oldest&language=de",
      "/api/app/surveys/s%201/responses/r1",
    ]);
  });

  it("starts an export, polls it, lists recent ones and points to its file (AWK-25)", async () => {
    const requests: Request[] = [];
    const item = { id: "e1", surveyId: "s1", status: "queued", format: "csv", done: 0, total: 0, file: null, error: null, requestedBy: "Anna Berger", byApiKey: false, attempt: null, retryAt: null, createdAt: "2026-10-15T08:00:00Z", finishedAt: null, expiresAt: null };
    const answers = [Response.json(item, { status: 202 }), Response.json({ ...item, status: "ready" }), Response.json({ items: [item] })];
    const client = createResultsClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    const started = await client.startExport("s 1", { format: "csv", values: "codes", status: "complete" });
    const polled = await client.exportOf("e1");
    const recent = await client.recentExports("s 1");

    expect([started.status, polled.status, recent.length]).toEqual(["queued", "ready", 1]);
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "POST /api/app/surveys/s%201/exports",
      "GET /api/app/exports/e1",
      "GET /api/app/surveys/s%201/exports",
    ]);
    expect(await requests[0]!.json()).toEqual({ format: "csv", values: "codes", status: "complete" });
    expect(client.exportFileUrl("e 1")).toBe("/api/app/exports/e%201/file");
  });
});
