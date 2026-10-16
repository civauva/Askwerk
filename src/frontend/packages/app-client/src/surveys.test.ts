// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { ApiError } from "./client";
import { createSurveysClient } from "./surveys";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

function fakeFetch(respond: () => Response) {
  const requests: Request[] = [];
  const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
    return respond();
  };

  return { requests, fetch };
}

const path = (request: Request | undefined) => {
  const url = new URL(request!.url);
  return url.pathname + url.search;
};

describe("createSurveysClient", () => {
  it("lists with status, search, sort, offset and limit in the query", async () => {
    const page = { items: [], counts: { all: 0, draft: 0, published: 0, closed: 0 }, total: 0 };
    const { requests, fetch } = fakeFetch(() => Response.json(page));
    const client = createSurveysClient({ fetch, telemetry });

    const list = await client.list({ status: "published", search: "Wien & Co", sort: "-title", offset: 50, limit: 25 });

    expect(list).toEqual(page);
    expect(path(requests[0])).toBe("/api/app/surveys?status=published&search=Wien+%26+Co&sort=-title&offset=50&limit=25");
  });

  it("reads, sets and drops the survey's own look, and creates a sample (W26)", async () => {
    const answers = [
      Response.json({ tokens: { layout: "card" }, own: false, lookBase: null, themeId: null }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
      Response.json({ items: [{ id: "cafe" }] }),
      Response.json({ id: "s9" }, { status: 201 }),
    ];
    const { requests, fetch } = fakeFetch(() => answers.shift()!);
    const client = createSurveysClient({ fetch, telemetry });

    expect((await client.look("s1")).own).toBe(false);
    await client.setLook("s1", { layout: "minimal" }, "preset:klar");
    await client.dropLook("s1");
    expect((await client.listSamples()).map((sample) => sample.id)).toEqual(["cafe"]);
    expect(await client.createSample("cafe", "Café visit")).toBe("s9");

    expect(requests.map((request) => `${request.method} ${path(request)}`)).toEqual([
      "GET /api/app/surveys/s1/look",
      "PUT /api/app/surveys/s1/look",
      "DELETE /api/app/surveys/s1/look",
      "GET /api/app/samples",
      "POST /api/app/samples/cafe",
    ]);
    expect(await requests[1]?.json()).toEqual({ tokens: { layout: "minimal" }, lookBase: "preset:klar" });
    expect(await requests[4]?.json()).toEqual({ title: "Café visit" });
  });

  it("lists everything without a query", async () => {
    const { requests, fetch } = fakeFetch(() => Response.json({ items: [], counts: {}, total: 0 }));
    const client = createSurveysClient({ fetch, telemetry });

    await client.list({});

    expect(path(requests[0])).toBe("/api/app/surveys");
  });

  it("creates a survey and returns its id", async () => {
    const { requests, fetch } = fakeFetch(() => Response.json({ id: "s1" }, { status: 201 }));
    const client = createSurveysClient({ fetch, telemetry });

    const id = await client.create({ title: "Kundenzufriedenheit 2026", languages: ["de", "en"], defaultLanguage: "de" });

    expect(id).toBe("s1");
    expect(requests[0]?.method).toBe("POST");
    expect(await requests[0]?.json()).toEqual({ title: "Kundenzufriedenheit 2026", languages: ["de", "en"], defaultLanguage: "de" });
  });

  it("is the designer's API: draft, save and publish", async () => {
    const answers = [
      Response.json({ surveyId: "s1", title: "T", versionNumber: 2, definition: { pages: [] }, published: null }),
      new Response(null, { status: 204 }),
      Response.json({ versionNumber: 2, links: [{ kind: "system", url: "https://surveys.acme.at/s/s1" }] }),
    ];
    const { requests, fetch } = fakeFetch(() => answers.shift()!);
    const client = createSurveysClient({ fetch, telemetry });

    const draft = await client.loadDraft("s1");
    await client.saveDraft("s1", { schemaVersion: 1, languages: ["de"], defaultLanguage: "de", pages: [] });
    const published = await client.publish("s1");

    expect(draft.versionNumber).toBe(2);
    expect(published.links[0]?.url).toBe("https://surveys.acme.at/s/s1");
    expect(requests.map((request) => `${request.method} ${path(request)}`)).toEqual([
      "GET /api/app/surveys/s1/draft",
      "PUT /api/app/surveys/s1/draft",
      "POST /api/app/surveys/s1/publish",
    ]);
    expect(await requests[1]?.json()).toEqual({ definition: { schemaVersion: 1, languages: ["de"], defaultLanguage: "de", pages: [] } });
  });

  it("puts the publish findings on the ApiError", async () => {
    const problem = { code: "publish-checks-failed", issues: [{ code: "text-missing", severity: "error", path: "pages/0/questions/0/text", language: "de" }], traceId: "t" };
    const { fetch } = fakeFetch(() => Response.json(problem, { status: 409 }));
    const client = createSurveysClient({ fetch, telemetry });

    const error = await client.publish("s1").catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).problem.issues).toEqual(problem.issues);
  });
});

describe("templates (AWK-17 step 5)", () => {
  it("lists templates, saves a draft as one, deletes one, and creates a survey from one", async () => {
    const requests: Request[] = [];
    const answers = [
      Response.json({ items: [{ id: "askwerk:nps-short", scope: "askwerk", name: null, names: { de: "NPS, kurz", en: "NPS, short" }, questions: 3, languages: ["de", "en"] }] }),
      Response.json({ id: "t1" }, { status: 201 }),
      new Response(null, { status: 204 }),
      Response.json({ id: "s2" }, { status: 201 }),
    ];
    const client = createSurveysClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry: { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined },
    });

    const templates = await client.listTemplates();
    await client.saveAsTemplate("s1", "Acme check-in");
    await client.deleteTemplate("t1");
    await client.create({ title: "NPS Q4", languages: [], defaultLanguage: "", templateId: "askwerk:nps-short" });

    expect(templates[0]?.names?.en).toBe("NPS, short");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/templates",
      "POST /api/app/templates",
      "DELETE /api/app/templates/t1",
      "POST /api/app/surveys",
    ]);
    expect(await requests[1]?.json()).toEqual({ surveyId: "s1", name: "Acme check-in" });
    expect(await requests[3]?.json()).toMatchObject({ templateId: "askwerk:nps-short" });
  });
});

describe("createSurveysClient: the survey's menu (AWK-29)", () => {
  it("reads the summary, renames, duplicates, closes, reopens and archives", async () => {
    const requests: Request[] = [];
    const summary = { id: "s1", title: "Survey", status: "published", publishedVersion: 3, hasChanges: false, languages: ["de"], responses: 248, participants: 640 };
    const answers = [Response.json(summary), new Response(null, { status: 204 }), Response.json({ id: "s2" }, { status: 201 }), new Response(null, { status: 204 }), new Response(null, { status: 204 }), new Response(null, { status: 204 })];
    const client = createSurveysClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    const read = await client.summary("s1");
    await client.rename("s1", "New title");
    const copy = await client.duplicate("s1", "Copy of Survey");
    await client.close("s1");
    await client.reopen("s1");
    await client.archive("s1");

    expect(read.participants).toBe(640);
    expect(copy).toBe("s2");
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/surveys/s1/summary",
      "PUT /api/app/surveys/s1/title",
      "POST /api/app/surveys/s1/duplicate",
      "POST /api/app/surveys/s1/close",
      "POST /api/app/surveys/s1/reopen",
      "POST /api/app/surveys/s1/archive",
    ]);
    expect(await requests[1]!.json()).toEqual({ title: "New title" });
    expect(await requests[2]!.json()).toEqual({ title: "Copy of Survey" });
  });
});

describe("createSurveysClient: links for the Share tab (AWK-29)", () => {
  it("reads the survey's links", async () => {
    const urls: string[] = [];
    const client = createSurveysClient({
      fetch: async (input) => {
        urls.push(String(input));
        return Response.json({ code: "kz26b7mq", links: [{ kind: "system", hostname: "surveys.acme.at", isPrimary: true, url: "https://surveys.acme.at/s/kz26b7mq" }] });
      },
      telemetry,
    });

    const links = await client.links("s 1");

    expect(urls).toEqual(["/api/app/surveys/s%201/links"]);
    expect(links[0]?.url).toBe("https://surveys.acme.at/s/kz26b7mq");
  });
});

describe("createSurveysClient: approvals (AWK-28)", () => {
  it("lists, approves with a note and withdraws", async () => {
    const requests: Request[] = [];
    const answers = [
      Response.json({ fingerprint: "7f3a", state: "approved", items: [{ id: "a1", by: "Ben Huber", at: "2026-10-15T08:50:00Z", note: null, fingerprint: "7f3a", version: 4, current: true, mine: true }] }),
      Response.json({ id: "a2" }, { status: 201 }),
      new Response(null, { status: 204 }),
    ];
    const client = createSurveysClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry: { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined },
    });

    const approvals = await client.approvals("s1");
    const id = await client.approve("s1", "checked with the works council");
    await client.withdrawApproval("s1", "a1");

    expect([approvals.state, approvals.items[0]?.by, id]).toEqual(["approved", "Ben Huber", "a2"]);
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/surveys/s1/approvals",
      "POST /api/app/surveys/s1/approvals",
      "DELETE /api/app/surveys/s1/approvals/a1",
    ]);
    expect(await requests[1]?.json()).toEqual({ note: "checked with the works council" });
  });
});
