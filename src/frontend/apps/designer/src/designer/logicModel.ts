// SPDX-License-Identifier: AGPL-3.0-only
// What the logic editor needs from a definition (AWK-31, W19): candidates, defaults, clean-up and order checks.
import { checkDefinition, type LogicCondition, type LogicGroup, type Page, type Question, type SurveyDefinition } from "@askwerk/schema";

export const allQuestionsOf = (definition: SurveyDefinition) => definition.pages.flatMap((page) => page.questions);

const answerable = (question: Question) => question.type !== "text-block" && question.type !== "image";

/** Questions a question's rule may use: answerable ones before it. */
export function questionCandidates(definition: SurveyDefinition, questionId: string): Question[] {
  const all = allQuestionsOf(definition);
  const index = all.findIndex((question) => question.id === questionId);

  return all.slice(0, Math.max(0, index)).filter(answerable);
}

/** Questions a page's rule may use: on earlier pages, and for "after this page" also on the page itself. */
export function pageCandidates(definition: SurveyDefinition, pageIndex: number, withOwn: boolean): Question[] {
  return definition.pages.slice(0, withOwn ? pageIndex + 1 : pageIndex).flatMap((page) => page.questions).filter(answerable);
}

/** A first condition on a question: its first value, or "is answered" when it has none to compare. */
export function defaultCondition(question: Question): LogicCondition {
  switch (question.type) {
    case "single":
    case "dropdown":
      return { question: question.id, op: "is", value: question.options?.[0]?.code ?? "" };
    case "multiple":
      return { question: question.id, op: "includes", value: question.options?.[0]?.code ?? "" };
    case "yes-no":
      return { question: question.id, op: "is", value: question.yes?.code ?? "1" };
    case "rating":
    case "nps":
    case "slider":
      return { question: question.id, op: "is", number: question.scale?.min ?? (question.type === "nps" ? 0 : 1) };
    case "number":
      return { question: question.id, op: "is", number: 0 };
    default:
      return { question: question.id, op: "answered" };
  }
}

/** Removes every condition on a question; groups left empty go, and so does an after rule that only had them. */
export function withoutConditionsOn(definition: SurveyDefinition, questionIds: readonly string[]): SurveyDefinition {
  const clean = (group: LogicGroup | undefined): LogicGroup | undefined => {
    if (group === undefined) {
      return undefined;
    }

    const conditions = group.conditions.filter((condition) => !questionIds.includes(condition.question));

    return conditions.length === 0 ? undefined : { ...group, conditions };
  };
  const drop = <T extends object, K extends keyof T>(item: T, key: K, value: T[K] | undefined): T => {
    const { [key]: _, ...rest } = item;
    return value === undefined ? (rest as T) : { ...item, [key]: value };
  };

  return {
    ...definition,
    pages: definition.pages.map((page) => {
      const questions = page.questions.map((question) => (question.showWhen === undefined ? question : drop(question, "showWhen", clean(question.showWhen))));
      let next: Page = drop({ ...page, questions }, "showWhen", clean(page.showWhen));

      if (page.after?.when !== undefined) {
        const when = clean(page.after.when);
        next = when === undefined ? drop(next, "after", undefined) : { ...next, after: { ...page.after, when } };
      }

      return next;
    }),
  };
}

/** How many rules use a question. */
export function rulesUsing(definition: SurveyDefinition, questionId: string): number {
  const uses = (group: LogicGroup | undefined) => (group?.conditions.some((condition) => condition.question === questionId) ? 1 : 0);

  return definition.pages.reduce((count, page) => count + uses(page.showWhen) + uses(page.after?.when) + page.questions.reduce((sum, question) => sum + uses(question.showWhen), 0), 0);
}

export type LogicNotice = { kind: "logic-order"; used: string; by: string } | { kind: "logic-jump" };

/** The first rule a change breaks: a condition now on a later question, or a jump now backwards. Null when none. */
export function brokenOrder(previous: SurveyDefinition, next: SurveyDefinition): LogicNotice | null {
  const before = new Set(checkDefinition(previous).map((issue) => `${issue.code} ${issue.path}`));
  const broken = checkDefinition(next).find((issue) => (issue.code === "logic-question-later" || issue.code === "logic-jump-backward") && !before.has(`${issue.code} ${issue.path}`));

  if (broken === undefined) {
    return null;
  }

  if (broken.code === "logic-jump-backward") {
    return { kind: "logic-jump" };
  }

  const parts = broken.path.split("/");
  const page = next.pages[Number(parts[1])]!;
  const owner = parts[2] === "questions" ? page.questions[Number(parts[3])]!.variable : `${Number(parts[1]) + 1}`;
  const group = parts[2] === "questions" ? page.questions[Number(parts[3])]!.showWhen : parts[2] === "after" ? page.after?.when : page.showWhen;
  const conditionIndex = Number(parts[parts.indexOf("conditions") + 1]);
  const usedId = group?.conditions[conditionIndex]?.question;

  return { kind: "logic-order", used: allQuestionsOf(next).find((question) => question.id === usedId)?.variable ?? usedId ?? "", by: owner };
}
