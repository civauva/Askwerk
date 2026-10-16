// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type SurveySummary } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SurveySettingsPage, type WorkspaceSurveys } from "./index";

afterEach(cleanup);

const published: SurveySummary = { id: "s1", title: "Customer satisfaction 2026", status: "published", publishedVersion: 3, hasChanges: false, languages: ["de", "en"], responses: 248, participants: 640 };

function fakeSurveys(summary: SurveySummary, closingDateAnswer: Promise<void> = Promise.resolve()) {
  const calls: unknown[][] = [];
  const surveys = {
    setClosingDate: (id: string, closesAt: string | null) => (calls.push(["closing-date", id, closesAt]), closingDateAnswer),
    setOneResponse: (id: string, on: boolean) => (calls.push(["one-response", id, on]), Promise.resolve()),
    summary: () => Promise.resolve(summary),
    links: () => Promise.resolve([]),
    approvals: () => Promise.resolve({ items: [] }),
    rename: (id: string, title: string) => (calls.push(["rename", id, title]), Promise.resolve()),
    duplicate: (id: string, title: string) => (calls.push(["duplicate", id, title]), Promise.resolve("s2")),
    saveAsTemplate: (id: string, name: string) => (calls.push(["template", id, name]), Promise.resolve()),
    close: (id: string) => (calls.push(["close", id]), Promise.resolve()),
    reopen: (id: string) => (calls.push(["reopen", id]), Promise.resolve()),
    archive: (id: string) => (calls.push(["archive", id]), Promise.resolve()),
  } as unknown as WorkspaceSurveys;

  return { calls, surveys };
}

async function shown(summary = published, canEdit = true, closingDateAnswer?: Promise<void>) {
  const { calls, surveys } = fakeSurveys(summary, closingDateAnswer);
  const handlers = { onTab: vi.fn(), onBack: vi.fn(), onOpenSurvey: vi.fn() };
  const view = render(<SurveySettingsPage surveys={surveys} surveyId="s1" locale="en" canEdit={canEdit} {...handlers} />);
  await screen.findByRole("heading", { name: "Survey settings" });

  return { calls, ...handlers, ...view };
}

describe("Survey settings (W23)", () => {
  it("renames the survey, shows its languages and whether it collects responses", async () => {
    const { calls, container } = await shown();
    const main = within(screen.getByRole("main"));

    expect(main.getByText("DE · EN")).toBeTruthy();
    expect(main.getByText("Open: the link takes responses.")).toBeTruthy();
    fireEvent.change(main.getByRole("textbox", { name: "Title (internal, not shown to respondents)" }), { target: { value: "Pulse Q4" } });
    fireEvent.click(main.getByRole("button", { name: "Save" }));
    await main.findByText("Saved");
    expect(calls).toContainEqual(["rename", "s1", "Pulse Q4"]);
    await expectNoAxeViolations(container);
  });

  it("duplicates the survey and opens the copy", async () => {
    const { calls, onOpenSurvey } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Duplicate" }));
    const dialog = within(screen.getByRole("dialog", { name: "Duplicate" }));
    expect((dialog.getByRole("textbox") as HTMLInputElement).value).toBe("Copy of Customer satisfaction 2026");
    fireEvent.click(dialog.getByRole("button", { name: "Duplicate" }));

    await waitFor(() => expect(onOpenSurvey).toHaveBeenCalledWith("s2"));
    expect(calls).toContainEqual(["duplicate", "s1", "Copy of Customer satisfaction 2026"]);
  });

  it("closes a published survey after asking, and archives it after asking", async () => {
    const { calls, onBack } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Close for responses…" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Close for responses" })).getByRole("button", { name: "Close for responses" }));
    await waitFor(() => expect(calls).toContainEqual(["close", "s1"]));
    fireEvent.click(screen.getByRole("button", { name: "Archive…" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Archive" })).getByRole("button", { name: "Archive" }));

    await waitFor(() => expect(onBack).toHaveBeenCalled());
    expect(calls).toContainEqual(["archive", "s1"]);
  });

  it("reopens a closed survey", async () => {
    const { calls } = await shown({ ...published, status: "closed" });

    fireEvent.click(screen.getByRole("button", { name: "Reopen for responses" }));

    await waitFor(() => expect(calls).toContainEqual(["reopen", "s1"]));
  });

  it("is read-only for readers", async () => {
    await shown(published, false);

    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByRole("button", { name: "Duplicate" })).toBeNull();
    expect(screen.queryByRole("button", { name: /^Archive/ })).toBeNull();
  });
});

describe("Collecting responses: closing date and one response per person (W24)", () => {
  const vienna = { ...published, closesAt: null, timeZone: "Europe/Vienna", oneResponsePerPerson: true };

  it("sets a closing date in the organisation's time zone (A1, A2)", async () => {
    const { calls, container } = await shown(vienna);
    const main = within(screen.getByRole("main"));

    expect(main.getByText("None")).toBeTruthy();
    fireEvent.click(main.getByRole("button", { name: "Set a closing date" }));
    expect(main.getByText("Time zone Europe/Vienna (this Askwerk's time zone). The survey closes by itself at this moment.")).toBeTruthy();
    fireEvent.change(main.getByLabelText("Closing date"), { target: { value: "2099-07-01T18:00" } });
    fireEvent.click(main.getByRole("button", { name: "Save closing date" }));

    await waitFor(() => expect(calls).toContainEqual(["closing-date", "s1", "2099-07-01T18:00"]));
    await expectNoAxeViolations(container);
  });

  it("explains a closing date in the past at the field (A2)", async () => {
    const past = Promise.reject(new ApiError(400, "4bf92f35", "invalid", { errors: { closesAt: ["closing-date-past"] } }));
    past.catch(() => undefined);
    await shown(vienna, true, past);
    const main = within(screen.getByRole("main"));

    fireEvent.click(main.getByRole("button", { name: "Set a closing date" }));
    fireEvent.change(main.getByLabelText("Closing date"), { target: { value: "2001-01-01T00:00" } });
    fireEvent.click(main.getByRole("button", { name: "Save closing date" }));

    expect((await main.findByRole("alert")).textContent).toBe("Pick a moment in the future.");
  });

  it("shows a set closing date in the status, changes it from its own value and removes it (A3)", async () => {
    const { calls } = await shown({ ...vienna, closesAt: "2099-07-01T16:00:00Z" });
    const main = within(screen.getByRole("main"));

    expect(main.getByText("Open until 1 July 2099, 18:00: the link takes responses.")).toBeTruthy();
    expect(main.getByText("1 Jul 2099, 18:00")).toBeTruthy();
    fireEvent.click(main.getByRole("button", { name: "Change" }));
    expect((main.getByLabelText("Closing date") as HTMLInputElement).value).toBe("2099-07-01T18:00");
    fireEvent.click(main.getByRole("button", { name: "Cancel" }));
    fireEvent.click(main.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(calls).toContainEqual(["closing-date", "s1", null]));
  });

  it("says when the closing date closed the survey, and that reopening removes it (A4)", async () => {
    await shown({ ...vienna, status: "closed", closesAt: "2020-07-01T16:00:00Z" });
    const main = within(screen.getByRole("main"));

    expect(main.getByText("Closed on 1 July 2020, 18:00 (closing date).")).toBeTruthy();
    expect(main.getByText("Reopening removes the passed closing date; set a new one if needed.")).toBeTruthy();
    expect(main.queryByRole("button", { name: "Set a closing date" })).toBeNull();
  });

  it("turns one response per person off and says it saved", async () => {
    const { calls } = await shown(vienna);
    const main = within(screen.getByRole("main"));
    const toggle = main.getByRole("switch", { name: "One response per person" });

    expect(toggle.getAttribute("aria-checked")).toBe("true");
    expect(main.getByText("After finishing, this browser cannot answer again. Personal invitation links always allow one response.")).toBeTruthy();
    fireEvent.click(toggle);

    await waitFor(() => expect(calls).toContainEqual(["one-response", "s1", false]));
    expect(await main.findByText("Saved")).toBeTruthy();
  });

  it("shows both settings as text to readers", async () => {
    await shown({ ...vienna, closesAt: "2099-07-01T16:00:00Z" }, false);
    const main = within(screen.getByRole("main"));

    expect(main.getByText("1 Jul 2099, 18:00")).toBeTruthy();
    expect(main.getByText("On")).toBeTruthy();
    expect(main.queryByRole("switch")).toBeNull();
    expect(main.queryByRole("button", { name: "Change" })).toBeNull();
  });

  it("says it in German", async () => {
    const { surveys } = fakeSurveys({ ...vienna, closesAt: "2099-07-01T16:00:00Z" });
    render(<SurveySettingsPage surveys={surveys} surveyId="s1" locale="de" canEdit onTab={vi.fn()} onBack={vi.fn()} />);
    const main = within(await screen.findByRole("main"));

    expect(await main.findByText("Offen bis 1. Juli 2099, 18:00: Der Link nimmt Antworten an.")).toBeTruthy();
    expect(main.getByRole("switch", { name: "Eine Antwort pro Person" })).toBeTruthy();
  });
});
