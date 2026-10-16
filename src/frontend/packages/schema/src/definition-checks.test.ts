// SPDX-License-Identifier: Apache-2.0
import { describe, expect, it } from "vitest";
import type { SurveyDefinition } from "./definition";
import { checkDefinition, variableRenames, type DefinitionIssue, type VariableRename } from "./definition-checks";

interface CorpusCase {
  readonly name: string;
  readonly definition: SurveyDefinition;
  readonly previous?: SurveyDefinition;
  readonly expected: DefinitionIssue[];
  readonly expectedRenames?: VariableRename[];
}

const corpus = Object.values(
  import.meta.glob<CorpusCase>("../tests/corpus/definition-checks/*.json", { eager: true, import: "default" }),
);

describe("checkDefinition (shared golden corpus)", () => {
  it.each(corpus.map((c) => [c.name, c] as const))("%s", (_name, c) => {
    expect(checkDefinition(c.definition, c.previous)).toEqual(c.expected);
  });
});

describe("variableRenames (shared golden corpus)", () => {
  const withPrevious = corpus.filter((c) => c.previous !== undefined);

  it.each(withPrevious.map((c) => [c.name, c] as const))("%s", (_name, c) => {
    expect(variableRenames(c.previous!, c.definition)).toEqual(c.expectedRenames);
  });
});
