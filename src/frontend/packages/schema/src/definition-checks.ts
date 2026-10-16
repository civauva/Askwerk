// SPDX-License-Identifier: Apache-2.0
import type { AnswerOption, LogicGroup, Question, QuestionType, SurveyDefinition, TranslatedText } from "./definition";

export type IssueSeverity = "error" | "warning";

/** One finding of the publish checks. Errors block publishing; warnings are shown prominently but do not block. */
export interface DefinitionIssue {
  readonly code: string;
  readonly severity: IssueSeverity;
  /** Where the issue is, as a slash path into the definition, e.g. "pages/0/questions/1/text". */
  readonly path: string;
  readonly language?: string;
}

/** A variable renamed since the last published version; it becomes a VariableMapping (data-model.md, rule 2). */
export interface VariableRename {
  readonly questionId: string;
  readonly from: string;
  readonly to: string;
}

/** Variable names as the inspector explains them: A–Z, 0–9 and _, starting with a letter, at most 32 characters. */
export const variableNamePattern = /^[A-Z][A-Z0-9_]{0,31}$/;

/** Question types whose answers are chosen from options; they need at least two. */
const choiceTypes: ReadonlySet<QuestionType> = new Set(["single", "multiple", "dropdown", "ranking", "matrix"]);

/** Question types that may offer "Other, please specify". */
const otherTypes: ReadonlySet<QuestionType> = new Set(["single", "multiple", "dropdown"]);

/** Most steps a rating may have per look (W7, panel 7); faces come in 3 or 5. */
const ratingStepLimits = { numbers: 11, stars: 10, dots: 10 } as const;

const datePatterns = {
  date: /^(\d{4}-\d{2}-\d{2}|today)$/,
  "date-time": /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/,
  time: /^\d{2}:\d{2}$/,
} as const;

/** The checks that run before publishing (M3 in AWK-5); the C# twin in Askwerk.Domain gives the same results. */
export function checkDefinition(definition: SurveyDefinition, previous?: SurveyDefinition): DefinitionIssue[] {
  const issues: DefinitionIssue[] = [];
  const error = (code: string, path: string) => issues.push({ code, severity: "error", path });
  const has = (text: TranslatedText | undefined, language: string) => (text?.[language] ?? "").trim() !== "";

  /** A required text must exist in the default language; any text that exists must exist in every survey language. */
  const checkText = (text: TranslatedText | undefined, path: string, required: boolean) => {
    if (!required && definition.languages.every((language) => !has(text, language))) {
      return;
    }

    if (!has(text, definition.defaultLanguage)) {
      issues.push({ code: "text-missing", severity: "error", path, language: definition.defaultLanguage });
      return;
    }

    for (const language of definition.languages) {
      if (language !== definition.defaultLanguage && !has(text, language)) {
        issues.push({ code: "translation-missing", severity: "warning", path, language });
      }
    }
  };

  /** Codes are unique within their list; each item's label is a required text. */
  const checkCodedList = (items: readonly AnswerOption[] | undefined, path: string, duplicateCode: string) => {
    const seenCodes = new Set<string>();

    items?.forEach((item, index) => {
      if (seenCodes.has(item.code)) {
        error(duplicateCode, `${path}/${index}/code`);
      }

      seenCodes.add(item.code);
      checkText(item.label, `${path}/${index}/label`, true);
    });
  };

  if (!definition.languages.includes(definition.defaultLanguage)) {
    error("default-language-missing", "defaultLanguage");
  }

  if (definition.pages.every((page) => page.questions.length === 0)) {
    error("survey-empty", "pages");
  }

  const seenVariables = new Set<string>();
  const publishedOwners = new Map(questionsOf(previous).map((question) => [question.variable, question.id]));

  definition.pages.forEach((page, pageIndex) => {
    checkText(page.title, `pages/${pageIndex}/title`, false);

    page.questions.forEach((question, questionIndex) => {
      const path = `pages/${pageIndex}/questions/${questionIndex}`;

      if (!variableNamePattern.test(question.variable)) {
        error("variable-invalid", `${path}/variable`);
      } else if (seenVariables.has(question.variable)) {
        error("variable-duplicate", `${path}/variable`);
      } else if ((publishedOwners.get(question.variable) ?? question.id) !== question.id) {
        error("variable-reused", `${path}/variable`);
      }

      seenVariables.add(question.variable);

      if ((question.type === "text-block" || question.type === "image") && question.required) {
        error("required-not-allowed", `${path}/required`);
      }

      // An image block's text is its caption, which may be left out.
      checkText(question.text, `${path}/text`, question.type !== "image");
      checkText(question.help, `${path}/help`, false);

      if (question.type === "image" && question.image === undefined) {
        error("image-missing", `${path}/image`);
      } else if (question.image !== undefined) {
        if (question.image.asset.trim() === "") {
          error("image-missing", `${path}/image/asset`);
        }

        checkText(question.image.alt, `${path}/image/alt`, true);
      }

      if (question.type === "rating" && (question.scale === undefined || question.scale.min >= question.scale.max)) {
        error("scale-invalid", `${path}/scale`);
      } else if (question.type === "rating" && question.scale !== undefined) {
        const steps = question.scale.max - question.scale.min + 1;

        if (question.display === "faces" ? steps !== 3 && steps !== 5 : steps > ratingStepLimits[question.display ?? "numbers"]) {
          error(question.display === "faces" ? "faces-need-three-or-five-steps" : "rating-steps-too-many", `${path}/scale`);
        }
      }

      if (question.type === "slider") {
        const scale = question.scale;
        const step = scale?.step ?? 1;

        if (scale === undefined || scale.min >= scale.max || step <= 0 || (scale.max - scale.min) % step !== 0) {
          error("slider-range-invalid", `${path}/scale`);
        }
      }

      checkText(question.scale?.minLabel, `${path}/scale/minLabel`, false);
      checkText(question.scale?.maxLabel, `${path}/scale/maxLabel`, false);

      if (choiceTypes.has(question.type) && (question.options?.length ?? 0) < 2) {
        error("options-missing", `${path}/options`);
      }

      checkCodedList(question.options, `${path}/options`, "option-code-duplicate");

      question.options?.forEach((option, index) => {
        if (option.image !== undefined && option.image.asset.trim() === "") {
          error("image-missing", `${path}/options/${index}/image`);
        }
      });

      if (question.other !== undefined) {
        const other = question.other;

        if (!otherTypes.has(question.type)) {
          error("other-not-allowed", `${path}/other`);
        } else {
          if (question.options?.some((option) => option.code === other.code)) {
            error("other-code-taken", `${path}/other/code`);
          }

          if (!variableNamePattern.test(other.variable)) {
            error("variable-invalid", `${path}/other/variable`);
          } else if (seenVariables.has(other.variable) || variablesOf(definition).includes(other.variable)) {
            error("other-variable-taken", `${path}/other/variable`);
          } else {
            // Reported once, here: the question that owns the name keeps it.
            seenVariables.add(other.variable);
          }
          checkText(other.label, `${path}/other/label`, true);
        }
      }

      if (question.type === "matrix" && (question.rows?.length ?? 0) === 0) {
        error("rows-missing", `${path}/rows`);
      }

      checkCodedList(question.rows, `${path}/rows`, "row-code-duplicate");

      if (question.type === "yes-no") {
        if ((question.yes?.code ?? "1") === (question.no?.code ?? "0")) {
          error("yes-no-codes-equal", `${path}/no/code`);
        }

        checkText(question.yes?.label, `${path}/yes/label`, false);
        checkText(question.no?.label, `${path}/no/label`, false);
      }

      if (question.type === "date") {
        const pattern = datePatterns[question.mode ?? "date"];
        const earliestValid = question.earliest === undefined || pattern.test(question.earliest);
        const latestValid = question.latest === undefined || pattern.test(question.latest);

        if (!earliestValid) {
          error("date-value-invalid", `${path}/earliest`);
        }

        if (!latestValid) {
          error("date-value-invalid", `${path}/latest`);
        }

        if (
          earliestValid &&
          latestValid &&
          question.earliest !== undefined &&
          question.latest !== undefined &&
          question.earliest !== "today" &&
          question.latest !== "today" &&
          question.earliest > question.latest
        ) {
          error("date-range-invalid", `${path}/latest`);
        }

        if (question.minuteStep !== undefined && (question.minuteStep < 1 || question.minuteStep > 60 || 60 % question.minuteStep !== 0)) {
          error("minute-step-invalid", `${path}/minuteStep`);
        }
      }

      checkText(question.placeholder, `${path}/placeholder`, false);
    });
  });

  // Logic (AWK-31): conditions use earlier questions and their codes; jumps go forward to a page that exists.
  const positions = new Map<string, { page: number; index: number; question: Question }>();
  definition.pages.forEach((page, pageIndex) => page.questions.forEach((question, index) => positions.set(question.id, { page: pageIndex, index, question })));
  const codesOf = (question: Question) =>
    question.type === "yes-no" ? [question.yes?.code ?? "1", question.no?.code ?? "0"] : [...(question.options ?? []).map((option) => option.code), ...(question.other ? [question.other.code] : [])];
  const checkGroup = (group: LogicGroup | undefined, path: string, earlier: (at: { page: number; index: number }) => boolean) =>
    group?.conditions.forEach((condition, conditionIndex) => {
      const at = `${path}/conditions/${conditionIndex}`;
      const found = positions.get(condition.question);

      if (found === undefined) {
        error("logic-question-missing", `${at}/question`);
        return;
      }

      if (!earlier(found)) {
        error("logic-question-later", `${at}/question`);
        return;
      }

      const codes = codesOf(found.question);

      if (codes.length > 0) {
        if (condition.value !== undefined && !codes.includes(condition.value)) {
          error("logic-option-missing", `${at}/value`);
        }

        condition.values?.forEach((value, valueIndex) => !codes.includes(value) && error("logic-option-missing", `${at}/values/${valueIndex}`));
      }
    });

  definition.pages.forEach((page, pageIndex) => {
    checkGroup(page.showWhen, `pages/${pageIndex}/showWhen`, (at) => at.page < pageIndex);
    page.questions.forEach((_question, index) =>
      checkGroup(page.questions[index]!.showWhen, `pages/${pageIndex}/questions/${index}/showWhen`, (at) => at.page < pageIndex || (at.page === pageIndex && at.index < index)),
    );

    if (page.after !== undefined) {
      checkGroup(page.after.when, `pages/${pageIndex}/after/when`, (at) => at.page <= pageIndex);

      if (page.after.goTo !== undefined) {
        const target = definition.pages.findIndex((each) => each.id === page.after!.goTo);

        if (target < 0) {
          error("logic-page-missing", `pages/${pageIndex}/after/goTo`);
        } else if (target <= pageIndex) {
          error("logic-jump-backward", `pages/${pageIndex}/after/goTo`);
        }
      }
    }
  });

  // A welcome page that is switched off is never shown, so its texts are not checked.
  if (definition.welcome?.enabled !== false) {
    checkText(definition.welcome?.title, "welcome/title", false);
    checkText(definition.welcome?.text, "welcome/text", false);
  }
  checkText(definition.endPage?.text, "endPage/text", false);

  if (definition.headerImage !== undefined) {
    checkText(definition.headerImage.alt, "headerImage/alt", true);
  }

  return issues;
}

/** Questions present in both versions whose variable name changed, in the order of the draft. */
export function variableRenames(previous: SurveyDefinition, definition: SurveyDefinition): VariableRename[] {
  const publishedNames = new Map(questionsOf(previous).map((question) => [question.id, question.variable]));

  return questionsOf(definition).flatMap((question) => {
    const from = publishedNames.get(question.id);

    return from === undefined || from === question.variable ? [] : [{ questionId: question.id, from, to: question.variable }];
  });
}

/** Every question variable of the definition (not the "other" text variables). */
function variablesOf(definition: SurveyDefinition) {
  return questionsOf(definition).map((question) => question.variable);
}

function questionsOf(definition: SurveyDefinition | undefined) {
  return definition?.pages.flatMap((page) => page.questions) ?? [];
}
