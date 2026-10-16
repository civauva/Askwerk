// SPDX-License-Identifier: AGPL-3.0-only
import type { Question, SurveyDefinition } from "@askwerk/schema";
import { describe, expect, it } from "vitest";
import { designerReducer, initialDesignerState, type DesignerState } from "./state";

function question(id: string, variable: string, overrides: Partial<Question> = {}): Question {
  return { id, variable, type: "short-text", text: { de: `Frage ${id}`, en: `Question ${id}` }, required: false, ...overrides };
}

function survey(...pages: (readonly Question[])[]): SurveyDefinition {
  return {
    schemaVersion: 1,
    languages: ["de", "en"],
    defaultLanguage: "de",
    pages: pages.map((questions, index) => ({ id: `p${index + 1}`, questions })),
  };
}

function stateOf(definition: SurveyDefinition, selectedQuestionId: string | null = null): DesignerState {
  return { ...initialDesignerState(definition, "de"), selectedQuestionId };
}

const ids = (state: DesignerState) => state.definition.pages.map((page) => page.questions.map((q) => q.id));

describe("designerReducer", () => {
  it("adds a question of the chosen type after the given one, names it with the next free variable and selects it", () => {
    const state = stateOf(survey([question("q1", "Q1"), question("q2", "Q2")]));

    const next = designerReducer(state, { type: "addQuestion", questionType: "long-text", pageId: "p1", afterQuestionId: "q1", newId: "q9" });

    expect(ids(next)).toEqual([["q1", "q9", "q2"]]);
    expect(next.definition.pages[0]?.questions[1]).toEqual({ id: "q9", variable: "Q3", type: "long-text", text: {}, required: false });
    expect(next.selectedQuestionId).toBe("q9");
  });

  it("adds a question at the start of a page when there is no question before it", () => {
    const state = stateOf(survey([question("q1", "Q1")], []));

    const next = designerReducer(state, { type: "addQuestion", questionType: "short-text", pageId: "p2", afterQuestionId: null, newId: "q9" });

    expect(ids(next)).toEqual([["q1"], ["q9"]]);
  });

  it("gives a new choice question two empty options so it can be filled in straight away", () => {
    const state = stateOf(survey([]));

    const next = designerReducer(state, { type: "addQuestion", questionType: "single", pageId: "p1", afterQuestionId: null, newId: "q9" });

    expect(next.definition.pages[0]?.questions[0]?.options).toEqual([
      { code: "1", label: {} },
      { code: "2", label: {} },
    ]);
  });

  it("gives a new rating question a scale from 1 to 5", () => {
    const state = stateOf(survey([]));

    const next = designerReducer(state, { type: "addQuestion", questionType: "rating", pageId: "p1", afterQuestionId: null, newId: "q9" });

    expect(next.definition.pages[0]?.questions[0]?.scale).toEqual({ min: 1, max: 5 });
  });

  it("gives a new matrix two options and one row", () => {
    const state = stateOf(survey([]));

    const next = designerReducer(state, { type: "addQuestion", questionType: "matrix", pageId: "p1", afterQuestionId: null, newId: "q9" });

    expect(next.definition.pages[0]?.questions[0]?.rows).toEqual([{ code: "R1", label: {} }]);
  });

  it("edits a text in one language and keeps the other languages", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "setText", questionId: "q1", field: "text", language: "en", value: "How satisfied are you?" });

    expect(next.definition.pages[0]?.questions[0]?.text).toEqual({ de: "Frage q1", en: "How satisfied are you?" });
  });

  it("adds a help text that did not exist yet", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "setText", questionId: "q1", field: "help", language: "de", value: "Letzter Besuch" });

    expect(next.definition.pages[0]?.questions[0]?.help).toEqual({ de: "Letzter Besuch" });
  });

  it("renames a variable and changes nothing else", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "setVariable", questionId: "q1", variable: "SAT_1" });

    expect(next.definition.pages[0]?.questions[0]).toEqual(question("q1", "SAT_1"));
  });

  it("marks a question as required", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "setRequired", questionId: "q1", required: true });

    expect(next.definition.pages[0]?.questions[0]?.required).toBe(true);
  });

  it("duplicates a question right after it with a new id and the next free variable, and selects the copy", () => {
    const state = stateOf(survey([question("q1", "Q1"), question("q2", "Q2")]));

    const next = designerReducer(state, { type: "duplicateQuestion", questionId: "q1", newId: "q9" });

    expect(ids(next)).toEqual([["q1", "q9", "q2"]]);
    expect(next.definition.pages[0]?.questions[1]).toEqual(question("q9", "Q3", { text: { de: "Frage q1", en: "Question q1" } }));
    expect(next.selectedQuestionId).toBe("q9");
  });

  it("deletes the selected question and selects the next one", () => {
    const state = stateOf(survey([question("q1", "Q1"), question("q2", "Q2")]), "q1");

    const next = designerReducer(state, { type: "deleteQuestion", questionId: "q1" });

    expect(ids(next)).toEqual([["q2"]]);
    expect(next.selectedQuestionId).toBe("q2");
  });

  it("selects the previous question when the last one is deleted", () => {
    const state = stateOf(survey([question("q1", "Q1"), question("q2", "Q2")]), "q2");

    const next = designerReducer(state, { type: "deleteQuestion", questionId: "q2" });

    expect(next.selectedQuestionId).toBe("q1");
  });

  it("selects nothing when the only question is deleted", () => {
    const state = stateOf(survey([question("q1", "Q1")]), "q1");

    const next = designerReducer(state, { type: "deleteQuestion", questionId: "q1" });

    expect(next.selectedQuestionId).toBeNull();
  });

  it("moves a question to another page at the given position", () => {
    const state = stateOf(survey([question("q1", "Q1"), question("q2", "Q2")], [question("q3", "Q3")]));

    const next = designerReducer(state, { type: "moveQuestion", questionId: "q1", toPageId: "p2", toIndex: 1 });

    expect(ids(next)).toEqual([["q2"], ["q3", "q1"]]);
  });

  it("moves a question within its page", () => {
    const state = stateOf(survey([question("q1", "Q1"), question("q2", "Q2"), question("q3", "Q3")]));

    const next = designerReducer(state, { type: "moveQuestion", questionId: "q3", toPageId: "p1", toIndex: 0 });

    expect(ids(next)).toEqual([["q3", "q1", "q2"]]);
  });

  it("adds an empty page at the end", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "addPage", newId: "p9" });

    expect(next.definition.pages.map((page) => page.id)).toEqual(["p1", "p9"]);
    expect(next.definition.pages[1]?.questions).toEqual([]);
  });

  it("switches the editing language without touching the definition", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "setLanguage", language: "en" });

    expect(next.language).toBe("en");
    expect(next.definition).toBe(state.definition);
  });

  it("selects a question", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "select", questionId: "q1" });

    expect(next.selectedQuestionId).toBe("q1");
  });
});

describe("designerReducer, question settings", () => {
  const choice = question("q1", "Q1", {
    type: "single",
    options: [
      { code: "1", label: { de: "Ja", en: "Yes" } },
      { code: "2", label: { de: "Nein", en: "No" } },
    ],
  });
  const first = (state: DesignerState) => state.definition.pages[0]!.questions[0]!;

  it("changes the type and keeps text, variable and the options a choice type can use", () => {
    const state = stateOf(survey([choice]));

    const next = designerReducer(state, { type: "setType", questionId: "q1", questionType: "multiple" });

    expect(first(next)).toEqual({ ...choice, type: "multiple" });
  });

  it("drops options and adds a scale when a choice becomes a rating", () => {
    const state = stateOf(survey([choice]));

    const next = designerReducer(state, { type: "setType", questionId: "q1", questionType: "rating" });

    expect(first(next)).toEqual({ id: "q1", variable: "Q1", type: "rating", text: choice.text, required: false, scale: { min: 1, max: 5 } });
  });

  it("gives a text question two empty options when it becomes a choice", () => {
    const state = stateOf(survey([question("q1", "Q1")]));

    const next = designerReducer(state, { type: "setType", questionId: "q1", questionType: "single" });

    expect(first(next).options).toEqual([
      { code: "1", label: {} },
      { code: "2", label: {} },
    ]);
  });

  it("turns Required off when a question becomes a text block", () => {
    const state = stateOf(survey([question("q1", "Q1", { required: true })]));

    const next = designerReducer(state, { type: "setType", questionId: "q1", questionType: "text-block" });

    expect(first(next).required).toBe(false);
  });

  it("edits an option label in one language", () => {
    const state = stateOf(survey([choice]));

    const next = designerReducer(state, { type: "setItemLabel", questionId: "q1", list: "options", index: 1, language: "en", value: "Not really" });

    expect(first(next).options?.[1]).toEqual({ code: "2", label: { de: "Nein", en: "Not really" } });
  });

  it("edits an option code", () => {
    const state = stateOf(survey([choice]));

    const next = designerReducer(state, { type: "setItemCode", questionId: "q1", list: "options", index: 0, code: "Y" });

    expect(first(next).options?.[0]?.code).toBe("Y");
  });

  it("adds an option with the next free number as its code", () => {
    const state = stateOf(survey([choice]));

    const next = designerReducer(state, { type: "addItem", questionId: "q1", list: "options" });

    expect(first(next).options?.[2]).toEqual({ code: "3", label: {} });
  });

  it("adds a matrix row with the next free row code", () => {
    const state = stateOf(survey([question("q1", "Q1", { type: "matrix", rows: [{ code: "R1", label: {} }] })]));

    const next = designerReducer(state, { type: "addItem", questionId: "q1", list: "rows" });

    expect(first(next).rows?.[1]).toEqual({ code: "R2", label: {} });
  });

  it("removes an option", () => {
    const state = stateOf(survey([choice]));

    const next = designerReducer(state, { type: "removeItem", questionId: "q1", list: "options", index: 0 });

    expect(first(next).options).toEqual([{ code: "2", label: { de: "Nein", en: "No" } }]);
  });

  it("sets the scale range", () => {
    const state = stateOf(survey([question("q1", "Q1", { type: "rating", scale: { min: 1, max: 5 } })]));

    const next = designerReducer(state, { type: "setScale", questionId: "q1", min: 0, max: 10 });

    expect(first(next).scale).toEqual({ min: 0, max: 10 });
  });

  it("sets a scale end label in one language", () => {
    const state = stateOf(survey([question("q1", "Q1", { type: "rating", scale: { min: 1, max: 5 } })]));

    const next = designerReducer(state, { type: "setScaleLabel", questionId: "q1", end: "maxLabel", language: "de", value: "Sehr zufrieden" });

    expect(first(next).scale).toEqual({ min: 1, max: 5, maxLabel: { de: "Sehr zufrieden" } });
  });

  it("switches randomised options and the Don't know answer", () => {
    const state = stateOf(survey([choice]));

    const randomized = designerReducer(state, { type: "setFlag", questionId: "q1", flag: "randomize", value: true });
    const next = designerReducer(randomized, { type: "setFlag", questionId: "q1", flag: "dontKnow", value: true });

    expect(first(next)).toEqual({ ...choice, randomize: true, dontKnow: true });
  });
});

describe("designerReducer: pages (AWK-29, W17 A)", () => {
  const three = () => stateOf(survey([question("q1", "Q1")], [question("q2", "Q2"), question("q3", "Q3")], [question("q4", "Q4")]));

  it("selects a page, which takes the selection from any question", () => {
    const next = designerReducer({ ...three(), selectedQuestionId: "q1" }, { type: "selectPage", pageId: "p2" });

    expect([next.selectedPageId, next.selectedQuestionId, next.settings]).toEqual(["p2", null, false]);
  });

  it("titles a page per language, and an empty title takes the language out", () => {
    const titled = designerReducer(three(), { type: "setPageTitle", pageId: "p2", language: "de", value: "Ihr Besuch" });
    const both = designerReducer(titled, { type: "setPageTitle", pageId: "p2", language: "en", value: "Your visit" });
    const cleared = designerReducer(both, { type: "setPageTitle", pageId: "p2", language: "de", value: "" });

    expect(both.definition.pages[1]?.title).toEqual({ de: "Ihr Besuch", en: "Your visit" });
    expect(cleared.definition.pages[1]?.title).toEqual({ en: "Your visit" });
  });

  it("moves a page up or down, with its questions", () => {
    const up = designerReducer(three(), { type: "movePage", pageId: "p2", toIndex: 0 });
    const last = designerReducer(three(), { type: "movePage", pageId: "p1", toIndex: 2 });

    expect(up.definition.pages.map((page) => page.id)).toEqual(["p2", "p1", "p3"]);
    expect(ids(up)).toEqual([["q2", "q3"], ["q1"], ["q4"]]);
    expect(last.definition.pages.map((page) => page.id)).toEqual(["p2", "p3", "p1"]);
  });

  it("duplicates a page after it, with new ids and free variable names, and selects the copy", () => {
    const next = designerReducer(three(), { type: "duplicatePage", pageId: "p2", newId: "p9", questionIds: ["q8", "q9"] });

    expect(next.definition.pages.map((page) => page.id)).toEqual(["p1", "p2", "p9", "p3"]);
    expect(next.definition.pages[2]?.questions.map((q) => [q.id, q.variable])).toEqual([["q8", "Q5"], ["q9", "Q6"]]);
    expect(next.selectedPageId).toBe("p9");
  });

  it("deletes a page with its questions, but never the last page", () => {
    const deleted = designerReducer({ ...three(), selectedPageId: "p2" }, { type: "deletePage", pageId: "p2" });
    const only = stateOf(survey([question("q1", "Q1")]));

    expect(ids(deleted)).toEqual([["q1"], ["q4"]]);
    expect(deleted.selectedPageId).toBe("p3");
    expect(designerReducer(only, { type: "deletePage", pageId: "p1" })).toBe(only);
  });
});
