// SPDX-License-Identifier: Apache-2.0
// Survey logic (AWK-31, W19): which pages and questions a respondent sees, and where the survey ends. The C# twin in
// Askwerk.Domain (SurveyLogic) gives the same results; both run the golden corpus in tests/corpus/logic.
import type { LogicCondition, LogicGroup, Page, Question, SurveyDefinition } from "./definition";

/** An answer as the runner keeps it; the same shape the App API stores. */
export interface LogicAnswer {
  readonly option?: string;
  readonly options?: readonly string[];
  readonly number?: number;
  readonly text?: string;
  readonly date?: string;
  readonly rows?: Readonly<Record<string, string>>;
  readonly order?: readonly string[];
  readonly dontKnow?: boolean;
}

export type LogicAnswers = Readonly<Record<string, LogicAnswer>>;

/** A page on a respondent's way, with the questions shown on it. */
export interface RoutedPage {
  readonly page: Page;
  readonly questions: readonly Question[];
}

const answered = (answer: LogicAnswer | undefined) =>
  answer !== undefined &&
  (answer.dontKnow === true ||
    (answer.option ?? "") !== "" ||
    (answer.options?.length ?? 0) > 0 ||
    answer.number !== undefined ||
    (answer.text ?? "").trim() !== "" ||
    (answer.date ?? "") !== "" ||
    Object.keys(answer.rows ?? {}).length > 0 ||
    (answer.order?.length ?? 0) > 0);

/** Whether one condition holds. An unanswered or "Don't know" answer matches only "not answered" (or "answered" for Don't know). */
export function conditionHolds(condition: LogicCondition, answers: LogicAnswers): boolean {
  const answer = answers[condition.question];

  if (condition.op === "answered") {
    return answered(answer);
  }

  if (condition.op === "not-answered") {
    return !answered(answer);
  }

  if (!answered(answer) || answer!.dontKnow === true) {
    return false;
  }

  const chosen = answer!.options ?? (answer!.option !== undefined ? [answer!.option] : []);
  const number = answer!.number;

  switch (condition.op) {
    case "is":
      return condition.number !== undefined ? number === condition.number : answer!.option === condition.value;
    case "is-not":
      return condition.number !== undefined ? number !== condition.number : answer!.option !== condition.value;
    case "is-one-of":
      return (condition.values ?? []).includes(answer!.option ?? "");
    case "includes":
      return chosen.includes(condition.value ?? "");
    case "includes-any":
      return (condition.values ?? []).some((code) => chosen.includes(code));
    case "includes-none":
      return !(condition.values ?? []).some((code) => chosen.includes(code));
    case "at-least":
      return number !== undefined && condition.number !== undefined && number >= condition.number;
    case "at-most":
      return number !== undefined && condition.number !== undefined && number <= condition.number;
    case "between":
      return number !== undefined && condition.number !== undefined && condition.to !== undefined && number >= condition.number && number <= condition.to;
  }
}

/** Whether a group holds: all or any of its conditions. An empty group holds. */
export function groupHolds(group: LogicGroup | undefined, answers: LogicAnswers): boolean {
  if (group === undefined || group.conditions.length === 0) {
    return true;
  }

  return group.match === "any" ? group.conditions.some((condition) => conditionHolds(condition, answers)) : group.conditions.every((condition) => conditionHolds(condition, answers));
}

/**
 * The respondent's way through the survey: shown pages with their shown questions, in order, until a rule ends the
 * survey. Answers to questions that are not shown count as not given, so a hidden question never steers the way.
 * Jumps go only forward; one to an unknown or earlier page is ignored.
 */
export function route(definition: SurveyDefinition, answers: LogicAnswers): RoutedPage[] {
  const routed: RoutedPage[] = [];
  const known: Record<string, LogicAnswer> = {};
  let index = 0;

  while (index < definition.pages.length) {
    const page = definition.pages[index]!;
    let next = index + 1;

    if (groupHolds(page.showWhen, known)) {
      const questions: Question[] = [];

      for (const question of page.questions) {
        if (groupHolds(question.showWhen, known)) {
          questions.push(question);

          if (answers[question.id] !== undefined) {
            known[question.id] = answers[question.id]!;
          }
        }
      }

      routed.push({ page, questions });

      if (page.after !== undefined && groupHolds(page.after.when, known)) {
        if (page.after.end === true) {
          break;
        }

        const target = page.after.goTo === undefined ? -1 : definition.pages.findIndex((each) => each.id === page.after!.goTo);

        if (target > index) {
          next = target;
        }
      }
    }

    index = next;
  }

  return routed;
}

/** Every condition of the definition with where it is, for checks and the overview (W19 C). */
export function logicRules(definition: SurveyDefinition) {
  return definition.pages.flatMap((page, pageIndex) => [
    ...(page.showWhen === undefined ? [] : [{ kind: "page" as const, pageIndex, page, group: page.showWhen }]),
    ...page.questions.flatMap((question, questionIndex) =>
      question.showWhen === undefined ? [] : [{ kind: "question" as const, pageIndex, page, questionIndex, question, group: question.showWhen }],
    ),
    ...(page.after === undefined ? [] : [{ kind: "after" as const, pageIndex, page, after: page.after, group: page.after.when }]),
  ]);
}

/** The questions a question's or page's logic depends on (its conditions' questions). */
export function dependsOn(group: LogicGroup | undefined): string[] {
  return [...new Set((group?.conditions ?? []).map((condition) => condition.question))];
}
