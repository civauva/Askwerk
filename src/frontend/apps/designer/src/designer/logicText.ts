// SPDX-License-Identifier: AGPL-3.0-only
// Logic in words and the conditions each question type offers (AWK-31, W19).
import { allQuestionsOf } from "./logicModel";
import type { LanguageCode, LogicCondition, LogicGroup, LogicOperator, PageAfter, Question, QuestionType, SurveyDefinition } from "@askwerk/schema";
import type { DesignerMessageKey } from "./messages";
import { displayText } from "./text";

type T = (key: DesignerMessageKey, values?: Record<string, string | number>) => string;

const choice: readonly LogicOperator[] = ["is", "is-not", "is-one-of", "answered", "not-answered"];
const several: readonly LogicOperator[] = ["includes", "includes-any", "includes-none", "answered", "not-answered"];
const numeric: readonly LogicOperator[] = ["is", "at-least", "at-most", "between", "answered", "not-answered"];
const yesNo: readonly LogicOperator[] = ["is", "answered", "not-answered"];
const any: readonly LogicOperator[] = ["answered", "not-answered"];

/** The conditions a question offers (W19 E). */
export function operatorsFor(type: QuestionType): readonly LogicOperator[] {
  switch (type) {
    case "single":
    case "dropdown":
      return choice;
    case "multiple":
      return several;
    case "rating":
    case "nps":
    case "slider":
    case "number":
      return numeric;
    case "yes-no":
      return yesNo;
    default:
      return any;
  }
}

export const operatorKey: Record<LogicOperator, DesignerMessageKey> = {
  is: "opIs",
  "is-not": "opIsNot",
  "is-one-of": "opIsOneOf",
  includes: "opIncludes",
  "includes-any": "opIncludesAny",
  "includes-none": "opIncludesNone",
  "at-least": "opAtLeast",
  "at-most": "opAtMost",
  between: "opBetween",
  answered: "opAnswered",
  "not-answered": "opNotAnswered",
};

/** Whether a question is answered by numbers (its conditions compare numbers). */
export const isNumeric = (question: Question) => question.type === "rating" || question.type === "nps" || question.type === "slider" || question.type === "number";

/** The codes and labels a condition can pick from: options (with Other), or Yes and No. */
export function choicesOf(t: T, question: Question, language: LanguageCode, definition: SurveyDefinition): { code: string; label: string }[] {
  if (question.type === "yes-no") {
    return [
      { code: question.yes?.code ?? "1", label: displayText(question.yes?.label, language, definition) ?? t("logicYes") },
      { code: question.no?.code ?? "0", label: displayText(question.no?.label, language, definition) ?? t("logicNo") },
    ];
  }

  return [
    ...(question.options ?? []).map((option) => ({ code: option.code, label: displayText(option.label, language, definition) ?? option.code })),
    ...(question.other ? [{ code: question.other.code, label: displayText(question.other.label, language, definition) ?? question.other.code }] : []),
  ];
}

function conditionText(t: T, condition: LogicCondition, definition: SurveyDefinition, language: LanguageCode) {
  const question = allQuestionsOf(definition).find((each) => each.id === condition.question);
  const variable = question?.variable ?? condition.question;
  const labels = question === undefined ? [] : choicesOf(t, question, language, definition);
  const label = (code: string | undefined) => labels.find((each) => each.code === code)?.label ?? code ?? "";
  const list = (codes: readonly string[] | undefined) => (codes ?? []).map(label).join(", ");
  const value = condition.number !== undefined ? String(condition.number) : label(condition.value);

  switch (condition.op) {
    case "is":
    case "is-not":
    case "at-least":
    case "at-most":
      return `${variable} ${t(operatorKey[condition.op])} ${value}`;
    case "includes":
      return `${variable} ${t("opIncludes")} ${label(condition.value)}`;
    case "is-one-of":
    case "includes-any":
    case "includes-none":
      return `${variable} ${t(operatorKey[condition.op])} ${list(condition.values)}`;
    case "between":
      return `${variable} ${t("opBetweenText", { from: condition.number ?? "", to: condition.to ?? "" })}`;
    default:
      return `${variable} ${t(operatorKey[condition.op])}`;
  }
}

/** A group's conditions in words: "SAT is at most 2 and BEFORE is Yes". */
export function conditionsText(t: T, group: LogicGroup, definition: SurveyDefinition, language: LanguageCode) {
  return group.conditions.map((condition) => conditionText(t, condition, definition, language)).join(group.match === "any" ? t("logicOr") : t("logicAnd"));
}

/** A page's after rule in words, or null when it just goes on. */
export function afterText(t: T, after: PageAfter, definition: SurveyDefinition, language: LanguageCode, pageName: (pageId: string) => string) {
  const conditions = after.when === undefined ? null : conditionsText(t, after.when, definition, language);

  if (after.end === true) {
    return conditions === null ? t("afterEndsAlways") : t("afterEndsWhen", { conditions });
  }

  if (after.goTo !== undefined) {
    return t("afterGoesWhen", { page: pageName(after.goTo), conditions: conditions ?? "" });
  }

  return null;
}
