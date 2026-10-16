// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveysClient, SurveySummary } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { primaryAction, statusText, SurveyBar, SurveyTabs, workspaceText } from "./index";

afterEach(cleanup);

const published: SurveySummary = { id: "s1", title: "Customer satisfaction 2026", status: "published", publishedVersion: 3, hasChanges: false, languages: ["de", "en"], responses: 248, participants: 640 };

function fakeSurveys() {
  const calls: unknown[][] = [];
  const surveys = {
    rename: (id: string, title: string) => (calls.push(["rename", id, title]), Promise.resolve()),
    duplicate: (id: string, title: string) => (calls.push(["duplicate", id, title]), Promise.resolve("s2")),
    close: (id: string) => (calls.push(["close", id]), Promise.resolve()),
    reopen: (id: string) => (calls.push(["reopen", id]), Promise.resolve()),
    archive: (id: string) => (calls.push(["archive", id]), Promise.resolve()),
  } as unknown as SurveysClient;

  return { calls, surveys };
}

function renderBar({ summary = published, canEdit = true, locale = "en" as "en" | "de" } = {}) {
  const { calls, surveys } = fakeSurveys();
  const handlers = { onBack: vi.fn(), onChanged: vi.fn(), onSearch: vi.fn(), onPreview: vi.fn(), onPrimary: vi.fn() };
  const view = render(
    <SurveyBar
      t={workspaceText(locale)}
      surveys={surveys}
      surveyId="s1"
      summary={summary}
      canEdit={canEdit}
      languages={["de", "en"]}
      language="en"
      onLanguage={() => undefined}
      onSearch={handlers.onSearch}
      onPreview={handlers.onPreview}
      onPrimary={handlers.onPrimary}
      onBack={handlers.onBack}
      onChanged={handlers.onChanged}
      profile={<button type="button">AB</button>}
    />,
  );

  return { calls, ...handlers, ...view };
}

describe("Breadcrumb in the sidebar layout (W31)", () => {
  it("names the survey, which opens its Questions, then the place as the page's heading", async () => {
    const { surveys } = fakeSurveys();
    const onHome = vi.fn();
    const { container } = render(
      <SurveyBar t={workspaceText("en")} surveys={surveys} surveyId="s1" summary={published} canEdit onPreview={vi.fn()} onPrimary={vi.fn()} onBack={vi.fn()} onChanged={vi.fn()} place="Share" onHome={onHome} />,
    );
    const crumbs = within(screen.getByRole("navigation", { name: "You are here" }));

    expect(crumbs.getByRole("heading", { level: 1, name: "Share" }).getAttribute("aria-current")).toBe("page");
    fireEvent.click(crumbs.getByRole("link", { name: published.title }));
    expect(onHome).toHaveBeenCalled();
    await expectNoAxeViolations(container);
  });
});

describe("primaryAction (W17)", () => {
  it("publishes a draft, publishes changes, and shares a published or closed survey; readers get none", () => {
    expect(primaryAction({ ...published, status: "draft", publishedVersion: null }, true)).toBe("publish");
    expect(primaryAction({ ...published, hasChanges: true }, true)).toBe("publishChanges");
    expect(primaryAction(published, true)).toBe("share");
    expect(primaryAction({ ...published, status: "closed" }, true)).toBe("share");
    expect(primaryAction(published, false)).toBeNull();
  });
});

describe("SurveyTabs (W17, W23, W25)", () => {
  it("shows the places in three named groups with counts, Settings at the end, and marks the current one", async () => {
    const onSelect = vi.fn();
    const { container } = render(<SurveyTabs t={workspaceText("en")} active="results" canEdit summary={published} onSelect={onSelect} />);

    const tabs = within(screen.getByRole("navigation", { name: "Survey" }));
    expect(tabs.getAllByRole("group").map((group) => within(group).getAllByRole("link").map((tab) => tab.textContent))).toEqual([["Questions", "Look"], ["Share", "Participants640"], ["Results248"]]);
    expect(tabs.getByRole("group", { name: "Create" })).toBeTruthy();
    expect(tabs.getByRole("group", { name: "Collect" })).toBeTruthy();
    expect(tabs.getByRole("group", { name: "Analyse" })).toBeTruthy();
    expect(tabs.getByRole("link", { name: "Results 248" }).getAttribute("aria-current")).toBe("page");
    fireEvent.click(tabs.getByRole("link", { name: "Look" }));
    fireEvent.click(tabs.getByRole("link", { name: "Survey settings" }));
    expect(onSelect).toHaveBeenCalledWith("settings");
    expect(onSelect).toHaveBeenCalledWith("survey-settings");
    await expectNoAxeViolations(container);
  });

  it("shows readers only Collect and Analyse", () => {
    render(<SurveyTabs t={workspaceText("de")} active="results" canEdit={false} summary={published} onSelect={() => undefined} />);

    const tabs = within(screen.getByRole("navigation", { name: "Umfrage" }));
    expect(tabs.queryByRole("group", { name: "Erstellen" })).toBeNull();
    expect(tabs.getAllByRole("link").map((tab) => tab.textContent)).toEqual(["Teilnehmende640", "Ergebnisse248", "Einstellungen"]);
  });
});

describe("SurveyBar (W17, W23)", () => {
  it("shows the way back, the name, the status, search, language, Preview, the primary action and the profile", async () => {
    const { onBack, onPrimary, container } = renderBar();
    const bar = within(screen.getByRole("banner"));

    expect(bar.getByRole("button", { name: "Customer satisfaction 2026, click to rename" })).toBeTruthy();
    expect(bar.getByText("Published · v3")).toBeTruthy();
    expect(bar.getByRole("button", { name: "Search" }).getAttribute("aria-keyshortcuts")).toBe("Control+K");
    expect(bar.getByRole("button", { name: "EN" }).getAttribute("aria-pressed")).toBe("true");
    expect(bar.getByRole("button", { name: /^Preview/ })).toBeTruthy();
    expect(bar.getByRole("button", { name: "AB" })).toBeTruthy();
    expect(bar.queryByRole("button", { name: "Survey settings" })).toBeNull();
    fireEvent.click(bar.getByRole("button", { name: "Share" }));
    fireEvent.click(bar.getByRole("link", { name: "Surveys" }));
    expect(onPrimary).toHaveBeenCalledWith("share");
    expect(onBack).toHaveBeenCalled();
    await expectNoAxeViolations(container);
  });

  it("renames the survey in place: Enter saves, Escape keeps the old name", async () => {
    const { calls, onChanged } = renderBar();

    fireEvent.click(screen.getByRole("button", { name: "Customer satisfaction 2026, click to rename" }));
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Title (internal, not shown to respondents)" }), { key: "Escape" });
    fireEvent.click(screen.getByRole("button", { name: "Customer satisfaction 2026, click to rename" }));
    const field = screen.getByRole("textbox", { name: "Title (internal, not shown to respondents)" });
    fireEvent.change(field, { target: { value: "Customer satisfaction 2027" } });
    fireEvent.keyDown(field, { key: "Enter" });

    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(calls).toEqual([["rename", "s1", "Customer satisfaction 2027"]]);
  });

  it("gives readers the name as plain text and no primary action", () => {
    renderBar({ canEdit: false });

    expect(screen.queryByRole("button", { name: /click to rename/ })).toBeNull();
    expect(within(screen.getByRole("banner")).getByText("Customer satisfaction 2026")).toBeTruthy();
    expect(within(screen.getByRole("banner")).queryByRole("button", { name: /^(Share|Publish)/ })).toBeNull();
  });
});

describe("Status with a closing date (W24 B)", () => {
  const summary: SurveySummary = { id: "s1", title: "Pulse", status: "published", publishedVersion: 3, hasChanges: false, languages: ["de", "en"], responses: 0, participants: 0, closesAt: "2099-07-01T16:00:00Z", timeZone: "Europe/Vienna" };

  it("says when a published survey closes", () => {
    expect(statusText(workspaceText("en"), summary)).toBe("Published · v3 · closes 1 Jul, 18:00");
    expect(statusText(workspaceText("de"), summary)).toBe("Veröffentlicht · v3 · endet 1. Juli, 18:00");
  });

  it("says on which day the closing date closed it", () => {
    expect(statusText(workspaceText("en"), { ...summary, status: "closed", closesAt: "2020-07-01T16:00:00Z" })).toBe("Closed 1 Jul");
  });
});
