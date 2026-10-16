// SPDX-License-Identifier: AGPL-3.0-only
import { checkDefinition, type LanguageCode, type SurveyDefinition, type TranslatedText } from "@askwerk/schema";
import { allQuestions } from "./state";
import { textIn } from "./text";

/** A text that is missing in one language: where it is, what it says in the default language. */
export interface MissingText {
  readonly path: string;
  readonly language: LanguageCode;
  /** The question it belongs to, if any. */
  readonly questionId: string | undefined;
  /** Short reference shown next to it: Q3, P2 (page title) or END (thank-you page). */
  readonly reference: string;
  readonly source: string;
}

/** Missing translations, from the same checks that run before publishing. */
export function missingTexts(definition: SurveyDefinition): MissingText[] {
  const questions = allQuestions(definition);

  return checkDefinition(definition)
    .filter((issue) => issue.code === "translation-missing" && issue.language !== undefined)
    .map((issue) => {
      const [first, pageIndex, second, questionIndex] = issue.path.split("/");
      const page = first === "pages" ? definition.pages[Number(pageIndex)] : undefined;
      const question = second === "questions" ? page?.questions[Number(questionIndex)] : undefined;
      const reference =
        question !== undefined ? `Q${questions.indexOf(question) + 1}` : page !== undefined ? `P${Number(pageIndex) + 1}` : "END";

      return {
        path: issue.path,
        language: issue.language!,
        questionId: question?.id,
        reference,
        source: textIn(textAt(definition, issue.path), definition.defaultLanguage) ?? "",
      };
    });
}

/** How many of the survey's texts exist in the language, out of all texts that exist in any language. */
export function translationProgress(definition: SurveyDefinition, language: LanguageCode): { present: number; total: number } {
  const texts = allTexts(definition).filter((text) => definition.languages.some((any) => textIn(text, any) !== undefined));

  return { present: texts.filter((text) => textIn(text, language) !== undefined).length, total: texts.length };
}

function allTexts(definition: SurveyDefinition): (TranslatedText | undefined)[] {
  return [
    ...definition.pages.flatMap((page) => [
      page.title,
      ...page.questions.flatMap((question) => [
        question.text,
        question.help,
        question.scale?.minLabel,
        question.scale?.maxLabel,
        ...(question.options ?? []).map((option) => option.label),
        ...(question.rows ?? []).map((row) => row.label),
        question.other?.label,
        question.yes?.label,
        question.no?.label,
        question.placeholder,
      ]),
    ]),
    definition.endPage?.text,
  ];
}

function textAt(definition: SurveyDefinition, path: string): TranslatedText | undefined {
  let node: unknown = definition;

  for (const part of path.split("/")) {
    node = node === undefined || node === null ? undefined : (node as Record<string, unknown>)[part];
  }

  return node as TranslatedText | undefined;
}
