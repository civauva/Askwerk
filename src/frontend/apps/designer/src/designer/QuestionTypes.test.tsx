// SPDX-License-Identifier: AGPL-3.0-only
// Question types 1–6 and the rating looks in the designer (AWK-20, W7 rev. 4).
import type { Question, SurveyDefinition } from "@askwerk/schema";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Designer } from "./Designer";
import { FakeDesignerApi, sampleDraft, sequentialIds } from "./testing";

afterEach(cleanup);

function draftWith(...questions: Question[]) {
  const definition: SurveyDefinition = { schemaVersion: 1, languages: ["de", "en"], defaultLanguage: "en", pages: [{ id: "p1", questions }] };

  return new FakeDesignerApi({ ...sampleDraft, definition });
}

async function open(api: FakeDesignerApi, select?: string) {
  render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} />);
  await screen.findByRole("navigation", { name: "Outline" });
  await act(async () => {});

  if (select !== undefined) {
    fireEvent.click(within(screen.getByRole("navigation", { name: "Outline" })).getByRole("button", { name: new RegExp(select) }));
  }
}

const inspector = () => within(screen.getByRole("complementary", { name: "Question settings" }));
const canvas = () => within(screen.getByRole("main", { name: "Canvas" }));
const saved = (api: FakeDesignerApi) => api.saved.at(-1)?.pages[0]?.questions;

/** Picks an option of a shared combo box (AWK-19): open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  const button = scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) });
  expect(button.getAttribute("aria-haspopup")).toBe("listbox");
  fireEvent.click(button);
  fireEvent.click(screen.getByRole("option", { name: option }));
}


function addFromMenu(name: string) {
  fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);
  fireEvent.click(screen.getByRole("option", { name: new RegExp(`^${name}`) }));
}

const base = { text: { en: "Question", de: "Frage" }, required: false } as const;

describe("Type menu (W7)", () => {
  it("offers the new types in groups", async () => {
    await open(draftWith({ id: "q1", variable: "Q1", type: "single", ...base, options: [] }));

    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);
    const menu = within(screen.getByRole("listbox", { name: "Question type" }));

    expect(menu.getAllByRole("group").map((group) => group.getAttribute("aria-label"))).toEqual(["Choice", "Scales", "Text and numbers", "Date and time", "Other"]);
    for (const name of ["Dropdown", "Yes / No", "Slider", "Email", "Phone", "Web address", "Date and time", "Time"]) {
      expect(menu.getByRole("option", { name: new RegExp(`^${name}`) })).toBeTruthy();
    }
  });

  it.each([
    ["Email", { type: "short-text", format: "email" }],
    ["Web address", { type: "short-text", format: "url" }],
    ["Date and time", { type: "date", mode: "date-time", minuteStep: 15 }],
    ["Time", { type: "date", mode: "time", minuteStep: 15 }],
    ["Yes / No", { type: "yes-no" }],
    ["Slider", { type: "slider", scale: { min: 0, max: 100, step: 1 } }],
    ["Dropdown", { type: "dropdown" }],
  ])("adds %s with what it needs", async (name, expected) => {
    const api = draftWith({ id: "q1", variable: "Q1", type: "long-text", ...base });
    await open(api);

    addFromMenu(name);

    await waitFor(() => expect(saved(api)?.find((question) => question.id === "new1")).toMatchObject(expected));
  });
});

describe("Type menu closing (AWK-19)", () => {
  it("closes on a click outside", async () => {
    await open(draftWith({ id: "q1", variable: "Q1", type: "long-text", ...base }));
    fireEvent.click(screen.getAllByRole("button", { name: "+ Add question" })[0]!);

    fireEvent.mouseDown(document.body);

    expect(screen.queryByRole("listbox", { name: "Question type" })).toBeNull();
  });
});

describe("Dropdown", () => {
  it("shows a closed list on the canvas and orders its options as chosen", async () => {
    const api = draftWith({ id: "q1", variable: "BRANCH", type: "dropdown", ...base, options: [{ code: "1", label: { en: "Vienna" } }, { code: "2", label: { en: "Graz" } }] });
    await open(api, "BRANCH");

    expect(canvas().getByText("Choose…")).toBeTruthy();
    fireEvent.click(inspector().getByRole("radio", { name: "Alphabetical" }));

    await waitFor(() => expect(saved(api)?.[0]?.order).toBe("alphabetical"));
  });
});

describe("“Other, please specify”", () => {
  it("adds an extra last option with a label in every survey language, its own code and text variable", async () => {
    const api = draftWith({ id: "q1", variable: "SOURCE", type: "multiple", ...base, options: [{ code: "1", label: { en: "Search" } }, { code: "2", label: { en: "Ads" } }] });
    await open(api, "SOURCE");

    fireEvent.click(inspector().getByRole("switch", { name: "“Other, please specify”" }));

    await waitFor(() =>
      expect(saved(api)?.[0]?.other).toEqual({ code: "OTHER", variable: "SOURCE_OTHER", label: { en: "Other, please specify:", de: "Sonstiges, bitte angeben:" } }),
    );
    expect(canvas().getByText("Other, please specify:")).toBeTruthy();
    fireEvent.change(inspector().getByRole("textbox", { name: "Text variable" }), { target: { value: "SOURCE_TEXT" } });
    await waitFor(() => expect(saved(api)?.[0]?.other?.variable).toBe("SOURCE_TEXT"));
  });

  it("is not offered for other types", async () => {
    await open(draftWith({ id: "q1", variable: "Q1", type: "rating", ...base, scale: { min: 1, max: 5 } }), "Q1");

    expect(inspector().queryByRole("switch", { name: "“Other, please specify”" })).toBeNull();
  });
});

describe("Yes / No", () => {
  it("shows two buttons and lets the codes change", async () => {
    const api = draftWith({ id: "q1", variable: "BEFORE", type: "yes-no", ...base });
    await open(api, "BEFORE");

    expect(canvas().getByText("Yes")).toBeTruthy();
    expect(canvas().getByText("No")).toBeTruthy();
    fireEvent.change(inspector().getByRole("textbox", { name: "Code of Yes" }), { target: { value: "Y" } });

    await waitFor(() => expect(saved(api)?.[0]?.yes).toEqual({ code: "Y" }));
  });
});

describe("Slider", () => {
  it("sets range, step and whether the value shows", async () => {
    const api = draftWith({ id: "q1", variable: "LIKELY", type: "slider", ...base, scale: { min: 0, max: 100, step: 1 } });
    await open(api, "LIKELY");

    fireEvent.change(inspector().getByRole("spinbutton", { name: "Step" }), { target: { value: "5" } });
    fireEvent.click(inspector().getByRole("switch", { name: "Show value" }));

    await waitFor(() => expect(saved(api)?.[0]?.scale).toEqual({ min: 0, max: 100, step: 5, showValue: true }));
    expect(canvas().getByText("Not answered yet")).toBeTruthy();
  });
});

describe("Short text formats", () => {
  it("checks against a format chosen in the inspector", async () => {
    const api = draftWith({ id: "q1", variable: "PHONE", type: "short-text", ...base });
    await open(api, "PHONE");

    fireEvent.click(inspector().getByRole("radio", { name: "Phone" }));

    await waitFor(() => expect(saved(api)?.[0]?.format).toBe("phone"));
    fireEvent.click(inspector().getByRole("radio", { name: "Free text" }));
    await waitFor(() => expect(saved(api)?.[0]).not.toHaveProperty("format"));
  });
});

describe("Date modes", () => {
  const type = (name: string, value: string) => {
    const field = inspector().getByRole("textbox", { name });
    fireEvent.change(field, { target: { value } });
    fireEvent.blur(field);
  };

  it("asks for a date, with earliest and latest picked as dates or “today”", async () => {
    const api = draftWith({ id: "q1", variable: "STARTED", type: "date", ...base });
    await open(api, "STARTED");

    type("Earliest", "1/1/2026");
    fireEvent.click(inspector().getAllByRole("checkbox", { name: "Today" })[1]!);

    await waitFor(() => expect(saved(api)?.[0]).toMatchObject({ earliest: "2026-01-01", latest: "today" }));
    expect(inspector().getAllByRole("button", { name: "Choose a date" })).toHaveLength(1);
  });

  it("asks for a date and time: earliest and latest are a date and a time", async () => {
    const api = draftWith({ id: "q1", variable: "STARTED", type: "date", ...base });
    await open(api, "STARTED");

    fireEvent.click(inspector().getByRole("radio", { name: "Date and time" }));
    fireEvent.change(inspector().getByRole("spinbutton", { name: "Minute step" }), { target: { value: "30" } });
    type("Latest date", "31/12/2026");
    type("Latest time", "18:30");

    await waitFor(() => expect(saved(api)?.[0]).toMatchObject({ mode: "date-time", minuteStep: 30, latest: "2026-12-31T18:30" }));
  });

  it("asks for a time only: earliest and latest are times", async () => {
    const api = draftWith({ id: "q1", variable: "STARTED", type: "date", ...base, mode: "time", minuteStep: 15 });
    await open(api, "STARTED");

    type("Earliest", "8");

    await waitFor(() => expect(saved(api)?.[0]).toMatchObject({ earliest: "08:00" }));
    expect(inspector().queryByRole("checkbox", { name: "Today" })).toBeNull();
  });
});

describe("Rating looks (W7, panel 7)", () => {
  it("shows stars and takes a custom range", async () => {
    const api = draftWith({ id: "q1", variable: "SAT", type: "rating", ...base, scale: { min: 1, max: 5 } });
    await open(api, "SAT");

    fireEvent.click(inspector().getByRole("radio", { name: "Stars" }));
    choose(inspector(), "Scale", "Custom…");
    fireEvent.change(inspector().getByRole("spinbutton", { name: "Min" }), { target: { value: "-3" } });
    fireEvent.change(inspector().getByRole("spinbutton", { name: "Max" }), { target: { value: "3" } });

    await waitFor(() => expect(saved(api)?.[0]).toMatchObject({ display: "stars", scale: { min: -3, max: 3 } }));
    expect(canvas().getAllByText("★")).toHaveLength(7);
  });

  it("has no accessibility violations with the settings open", async () => {
    await open(draftWith({ id: "q1", variable: "SAT", type: "rating", ...base, scale: { min: 1, max: 5 }, display: "faces" }), "SAT");

    await expectNoAxeViolations(document.body);
  });
});
