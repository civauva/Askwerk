// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyDefinition } from "@askwerk/schema";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Designer } from "./Designer";
import { designerReducer, initialDesignerState } from "./state";
import { FakeDesignerApi, sampleDraft, sequentialIds } from "./testing";

afterEach(cleanup);

const improveWhenLow = { match: "all" as const, conditions: [{ question: "q2", op: "at-most" as const, number: 2 }] };
const withRule: SurveyDefinition = {
  ...sampleDraft.definition,
  pages: sampleDraft.definition.pages.map((page) => ({ ...page, questions: page.questions.map((question) => (question.id === "q3" ? { ...question, showWhen: improveWhenLow } : question)) })),
};

async function loaded(api = new FakeDesignerApi()) {
  const view = render(<Designer api={api} surveyId="s1" initialLocale="en" newId={sequentialIds()} autosaveDelayMs={0} />);
  await screen.findByRole("navigation", { name: "Outline" });
  await act(async () => {});

  return { api, ...view };
}

const outline = () => within(screen.getByRole("navigation", { name: "Outline" }));
const inspector = () => within(screen.getByRole("complementary", { name: /settings$/ }));
const choose = (label: RegExp, option: string) => {
  fireEvent.click(inspector().getByRole("button", { name: label }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};

describe("A question's logic (AWK-31, W19 A)", () => {
  it("shows a question only when an earlier answer says so, and reads the rule back", { timeout: 40_000 }, async () => {
    const { api, container } = await loaded();

    fireEvent.click(outline().getByRole("button", { name: /What could we improve/ }));
    fireEvent.click(inspector().getByRole("radio", { name: "Only when …" }));
    expect(inspector().getByRole("button", { name: /^Condition 1: question/ }).textContent).toContain("SAT_1");
    choose(/^Condition 1: condition/, "is at most");
    fireEvent.change(inspector().getByRole("spinbutton", { name: "Condition 1: number" }), { target: { value: "2" } });

    expect(inspector().getByText("Reads: Shown when SAT_1 is at most 2.")).toBeTruthy();
    expect(within(screen.getByRole("article", { name: /IMPROVE/ })).getByText("⑂ Shown when SAT_1 is at most 2")).toBeTruthy();
    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.questions[1]?.showWhen).toEqual(improveWhenLow));
    await expectNoAxeViolations(container);
  });

  it("offers no logic for the first question, as there is nothing before it", async () => {
    await loaded();

    fireEvent.click(outline().getByRole("button", { name: /How satisfied/ }));

    expect((inspector().getByRole("radio", { name: "Only when …" }) as HTMLInputElement).disabled).toBe(true);
    expect(inspector().getByText("Logic uses questions before this one; there are none yet.")).toBeTruthy();
  });

  it("asks before deleting a question that a rule uses, and takes its conditions out", async () => {
    const { api } = await loaded(new FakeDesignerApi({ ...sampleDraft, definition: withRule }));

    fireEvent.click(outline().getByRole("button", { name: /How satisfied/ }));
    fireEvent.click(within(screen.getByRole("article", { name: /SAT_1/ })).getByRole("button", { name: "Delete" }));
    const ask = within(screen.getByRole("dialog", { name: "Delete question" }));
    expect(ask.getByText("SAT_1 is used in 1 rule. Delete the question and those conditions?")).toBeTruthy();
    fireEvent.click(ask.getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.questions.map((question) => question.id)).toEqual(["q3", "q4"]));
    expect(api.saved.at(-1)?.pages[1]?.questions[0]?.showWhen).toBeUndefined();
  });
});

describe("A page's logic (AWK-31, W19 B)", () => {
  it("ends the survey after a page", async () => {
    const { api } = await loaded();

    fireEvent.click(outline().getByRole("button", { name: "02 · Your visit" }));
    fireEvent.click(inspector().getByRole("radio", { name: "End the survey" }));

    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.after).toEqual({ end: true }));
  });

  it("jumps forward under a condition", async () => {
    const { api } = await loaded();

    fireEvent.click(outline().getByRole("button", { name: "02 · Your visit" }));
    fireEvent.click(inspector().getByRole("radio", { name: "Jump under a condition …" }));
    choose(/^Then/, "End the survey");

    await waitFor(() => expect(api.saved.at(-1)?.pages[1]?.after).toEqual({ when: { match: "all", conditions: [{ question: "q2", op: "is", number: 1 }] }, end: true }));
    expect(inspector().getByText("Reads: Ends the survey when SAT_1 is 1.")).toBeTruthy();
  });
});

describe("Every rule at one glance (AWK-31, W19 C)", () => {
  it("lists the rules under All rules at the foot of the page list and opens one where it lives (W23)", async () => {
    await loaded(new FakeDesignerApi({ ...sampleDraft, definition: withRule }));

    fireEvent.click(within(screen.getByRole("navigation", { name: "Outline" })).getByRole("button", { name: /^All rules/ }));
    const rules = within(screen.getByRole("region", { name: "Logic · 1 rule" }));
    expect(rules.getByText("Shown when SAT_1 is at most 2.")).toBeTruthy();
    fireEvent.click(rules.getByRole("button", { name: "Edit the rule of IMPROVE" }));

    expect(screen.getByRole("complementary", { name: "Question settings" })).toBeTruthy();
  });
});

describe("Moves that would break logic (AWK-31, W19 D)", () => {
  it("refuses to move a question below one that depends on it, and says why", () => {
    const state = initialDesignerState(withRule, "en");

    const moved = designerReducer(state, { type: "moveQuestion", questionId: "q2", toPageId: "p3", toIndex: 0 });

    expect(moved.definition).toBe(state.definition);
    expect(moved.notice).toEqual({ kind: "logic-order", used: "SAT_1", by: "IMPROVE" });
  });
});
