// SPDX-License-Identifier: Apache-2.0
import { describe, expect, it } from "vitest";
import type { SurveyDefinition } from "./definition";
import { route, type LogicAnswers } from "./logic";

interface CorpusCase {
  readonly name: string;
  readonly definition: SurveyDefinition;
  readonly answers: LogicAnswers;
  readonly expected: { readonly page: string; readonly questions: readonly string[] }[];
}

const corpus = Object.values(import.meta.glob<CorpusCase>("../tests/corpus/logic/*.json", { eager: true, import: "default" }));

describe("route (shared golden corpus, AWK-31)", () => {
  it.each(corpus.map((c) => [c.name, c] as const))("%s", (_name, c) => {
    expect(route(c.definition, c.answers).map((routed) => ({ page: routed.page.id, questions: routed.questions.map((question) => question.id) }))).toEqual(c.expected);
  });
});
