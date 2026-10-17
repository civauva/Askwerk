// SPDX-License-Identifier: AGPL-3.0-only
import type { EmbedSession, KeyResult, SurveyList, SurveyKeyResult } from "@askwerk/app-client";
import { readHostLook } from "@askwerk/ui";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { EmbeddedApp, type HostEvent } from "./EmbeddedApp";
import { EmbeddedList } from "./EmbeddedList";
import { EmbeddedResult } from "./EmbeddedResult";
import { keySummary } from "./keys";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const rating: KeyResult = {
  question: "SAT",
  text: { en: "How satisfied are you with our service?" },
  type: "rating",
  auto: false,
  answered: 412,
  value: 4.1,
  max: 5,
  top: null,
  spread: [
    { code: "1", label: null, count: 20 },
    { code: "2", label: null, count: 17 },
    { code: "3", label: null, count: 54 },
    { code: "4", label: null, count: 132 },
    { code: "5", label: null, count: 189 },
  ],
};

const list = {
  items: [
    { id: "s1", title: "Customer satisfaction 2026", status: "published", publishedVersion: 3, hasChanges: false, languages: ["en"], createdBy: "Anna", updatedAt: "2026-10-10T08:00:00Z", responses: 412, front: { title: null, text: null, defaultLanguage: "en", look: {}, code: "k7m2pqzr", publishedAt: "2026-09-02T08:00:00Z", keys: [rating] } },
    { id: "s2", title: "Service desk, Q4", status: "draft", publishedVersion: null, hasChanges: false, languages: ["en"], createdBy: "Anna", updatedAt: "2026-10-16T08:00:00Z", responses: 0, front: { title: null, text: null, defaultLanguage: "en", look: {}, keys: [] } },
  ],
  counts: { all: 2, draft: 1, published: 1, closed: 0 },
  total: 2,
} as unknown as SurveyList;

describe("Drop-in components (W37)", () => {
  it("reads only safe host looks from the frame's address", () => {
    expect(readHostLook(`#look=${encodeURIComponent(JSON.stringify({ accent: "#1456b8", background: "#f3f5f9", font: "Segoe UI, sans-serif", radius: 4 }))}`)).toEqual({ accent: "#1456b8", background: "#f3f5f9", font: "Segoe UI, sans-serif", radius: 4 });
    expect(readHostLook(`#look=${encodeURIComponent(JSON.stringify({ accent: "red;}body{display:none", font: "</style>", radius: 99 }))}`)).toEqual({ radius: 24 });
    expect(readHostLook("")).toBeNull();
  });

  it("shows a scale as the share of its top two values", () => {
    expect(keySummary(rating)).toEqual({ kind: "scale", percent: 78, range: "4–5" });
  });

  it("lists the surveys with code, answers and key result; Results, Edit and New survey go to the host", async () => {
    const post = vi.fn();
    const { container } = render(<EmbeddedList surveys={{ list: () => Promise.resolve(list) }} locale="en" post={post} />);
    const table = await screen.findByRole("table", { name: "Surveys" });
    const published = within(within(table).getByText("Customer satisfaction 2026").closest("tr")!);

    expect(published.getByText("k7m2pqzr · published Sep 2")).toBeTruthy();
    expect(published.getByText("Satisfied 78 %")).toBeTruthy();
    expect(within(within(table).getByText("Service desk, Q4").closest("tr")!).queryByRole("button", { name: /Results of/ })).toBeNull();
    fireEvent.click(published.getByRole("button", { name: "Results of Customer satisfaction 2026" }));
    fireEvent.click(screen.getByRole("button", { name: "+ New survey" }));
    expect(post).toHaveBeenCalledWith({ type: "askwerk:open", surveyId: "s1", view: "results" });
    expect(post).toHaveBeenCalledWith({ type: "askwerk:create" });
    await expectNoAxeViolations(container);
  });

  it("shows one key result, live", async () => {
    const result: SurveyKeyResult = { title: "Customer satisfaction 2026", responses: 412, complete: 400, key: rating };
    render(<EmbeddedResult load={() => Promise.resolve(result)} locale="en" />);

    expect((await screen.findByRole("heading", { level: 1 })).textContent).toBe("How satisfied are you with our service?");
    expect(screen.getByText("78 %", { exact: false }).textContent).toBe("78 % satisfied (4–5)");
    expect(screen.getByText("412 answers · live")).toBeTruthy();
  });

  it("starts a list session and follows the host's look", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(new Response(JSON.stringify(list), { status: 200, headers: { "Content-Type": "application/json" } }))));
    const session: EmbedSession = { token: "aws_x", expiresAt: "2026-10-15T13:00:00Z", endsAt: "2026-10-15T20:00:00Z", surveyId: "00000000-0000-0000-0000-000000000000", view: "list", language: "en" };
    const start = vi.fn(() => Promise.resolve(session));
    const post = vi.fn<(event: HostEvent) => void>();
    render(<EmbeddedApp token="eyJ.a.b" view="list" look={{ accent: "#1456b8", radius: 4 }} lang="en" telemetry={{ error: vi.fn(), event: vi.fn(), metric: vi.fn() } as never} post={post} start={start} renew={() => Promise.resolve(session)} />);

    await screen.findByRole("table", { name: "Surveys" });
    expect(start).toHaveBeenCalledWith(expect.anything(), "eyJ.a.b", "list");
    expect(document.documentElement.style.getPropertyValue("--aw-signal")).toBe("#1456b8");
    expect(document.documentElement.style.getPropertyValue("--aw-radius-control")).toBe("4px");
    await waitFor(() => expect(post).toHaveBeenCalledWith({ type: "askwerk:ready", surveyId: session.surveyId, view: "list" }));
  });
});
