// SPDX-License-Identifier: AGPL-3.0-only
import type { AnswerOption, LanguageCode, LogicGroup, Page, PageAfter, Question, QuestionType, SurveyDefinition, TranslatedText } from "@askwerk/schema";
import { brokenOrder, withoutConditionsOn, type LogicNotice } from "./logicModel";

/** What the designer holds in memory: the draft definition, the selection and the language being edited. */
export interface DesignerState {
  readonly definition: SurveyDefinition;
  readonly selectedQuestionId: string | null;
  /** A selected page (AWK-29): its header on the canvas and its settings in the inspector. */
  readonly selectedPageId?: string | null | undefined;
  readonly language: LanguageCode;
  /** Create → Look is open (W23; was the settings, W9 A): the canvas shows the survey as respondents see it. */
  readonly settings: boolean;
  /** The welcome page, the thank-you page or all rules is selected in the page list (W23). */
  readonly edge?: "welcome" | "end" | "rules" | null | undefined;
  /** Why the last change was refused, e.g. a move that would break logic (AWK-31). */
  readonly notice?: LogicNotice | null;
}

/** Survey texts outside the questions (W9): the welcome page's title and text, and the thank-you text. */
export type SurveyTextField = "welcomeTitle" | "welcomeText" | "endText";

export type DesignerAction =
  | { type: "select"; questionId: string | null }
  | { type: "openSettings" }
  | { type: "selectEdge"; edge: "welcome" | "end" | "rules" }
  | { type: "setLayout"; layout: NonNullable<SurveyDefinition["layout"]> }
  | { type: "setWelcomeEnabled"; enabled: boolean }
  | { type: "setSurveyText"; field: SurveyTextField; language: LanguageCode; value: string }
  | { type: "setHeaderImage"; asset: string | null }
  | { type: "setOptionImage"; questionId: string; index: number; asset: string | null }
  | { type: "setHeaderAlt"; language: LanguageCode; value: string }
  | { type: "setLanguage"; language: LanguageCode }
  | { type: "addQuestion"; questionType: QuestionType; pageId: string; afterQuestionId: string | null; newId: string; extra?: Partial<Question> }
  | { type: "duplicateQuestion"; questionId: string; newId: string }
  | { type: "deleteQuestion"; questionId: string }
  | { type: "moveQuestion"; questionId: string; toPageId: string; toIndex: number }
  | { type: "setText"; questionId: string; field: "text" | "help"; language: LanguageCode; value: string }
  | { type: "setVariable"; questionId: string; variable: string }
  | { type: "setRequired"; questionId: string; required: boolean }
  | { type: "addPage"; newId: string }
  | { type: "selectPage"; pageId: string }
  | { type: "setPageTitle"; pageId: string; language: LanguageCode; value: string }
  | { type: "movePage"; pageId: string; toIndex: number }
  | { type: "duplicatePage"; pageId: string; newId: string; questionIds: readonly string[] }
  | { type: "deletePage"; pageId: string }
  | { type: "setPageLogic"; pageId: string; showWhen?: LogicGroup | null; after?: PageAfter | null }
  | { type: "setType"; questionId: string; questionType: QuestionType }
  | { type: "setItemLabel"; questionId: string; list: ItemList; index: number; language: LanguageCode; value: string }
  | { type: "setItemCode"; questionId: string; list: ItemList; index: number; code: string }
  | { type: "addItem"; questionId: string; list: ItemList }
  | { type: "removeItem"; questionId: string; list: ItemList; index: number }
  | { type: "setScale"; questionId: string; min: number; max: number }
  | { type: "setScaleLabel"; questionId: string; end: "minLabel" | "maxLabel"; language: LanguageCode; value: string }
  | { type: "setFlag"; questionId: string; flag: "randomize" | "dontKnow"; value: boolean }
  /** Sets type settings (W7); keys listed in remove are taken out, so an empty setting leaves no trace in the definition. */
  | { type: "patch"; questionId: string; patch: Partial<Question>; remove?: readonly (keyof Question)[] };

/** The coded lists of a question: answer options, and the rows of a matrix. */
export type ItemList = "options" | "rows";

export function initialDesignerState(definition: SurveyDefinition, language: LanguageCode): DesignerState {
  return { definition, selectedQuestionId: null, language, settings: false };
}

/**
 * All edits of the draft as pure state changes; ids come in with the action so the result is deterministic. A move
 * that would put a question below one whose rule uses it, or make a page jump backwards, is refused with a notice.
 */
export function designerReducer(state: DesignerState, action: DesignerAction): DesignerState {
  const next = reduce(state.notice ? { ...state, notice: null } : state, action);

  if ((action.type === "moveQuestion" || action.type === "movePage") && next.definition !== state.definition) {
    const broken = brokenOrder(state.definition, next.definition);

    if (broken !== null) {
      return { ...state, notice: broken };
    }
  }

  return next;
}

function reduce(state: DesignerState, action: DesignerAction): DesignerState {
  switch (action.type) {
    case "select":
      return { ...state, selectedQuestionId: action.questionId, selectedPageId: null, settings: false, edge: null };

    case "openSettings":
      return { ...state, selectedQuestionId: null, selectedPageId: null, settings: true, edge: null };

    case "selectEdge":
      return { ...state, selectedQuestionId: null, selectedPageId: null, settings: false, edge: action.edge };

    case "selectPage":
      return { ...state, selectedQuestionId: null, selectedPageId: action.pageId, settings: false, edge: null };

    case "setPageTitle": {
      const pages = state.definition.pages.map((page) => {
        if (page.id !== action.pageId) {
          return page;
        }

        const { [action.language]: _old, ...others } = page.title ?? {};
        const title = action.value.trim() === "" ? others : { ...others, [action.language]: action.value };
        const { title: _title, ...rest } = page;

        return Object.keys(title).length === 0 ? rest : { ...rest, title };
      });

      return { ...state, definition: { ...state.definition, pages } };
    }

    case "movePage": {
      const from = state.definition.pages.findIndex((page) => page.id === action.pageId);

      if (from < 0) {
        return state;
      }

      const pages = [...state.definition.pages];
      const [moving] = pages.splice(from, 1);
      pages.splice(Math.max(0, Math.min(pages.length, action.toIndex)), 0, moving!);

      return { ...state, definition: { ...state.definition, pages } };
    }

    case "duplicatePage": {
      const index = state.definition.pages.findIndex((page) => page.id === action.pageId);
      const original = state.definition.pages[index];

      if (original === undefined) {
        return state;
      }

      // Each copy takes the next free variable; the copies made so far count as taken.
      let definition = state.definition;
      const questions = original.questions.map((question, position) => {
        const copy: Question = { ...question, id: action.questionIds[position] ?? `${question.id}-copy`, variable: nextFreeVariable(definition) };
        definition = { ...definition, pages: [...definition.pages, { id: "__counting", questions: [copy] }] };
        return copy;
      });
      const pages = [...state.definition.pages];
      pages.splice(index + 1, 0, { ...original, id: action.newId, questions });

      return { ...state, definition: { ...state.definition, pages }, selectedQuestionId: null, selectedPageId: action.newId };
    }

    case "deletePage": {
      const pages = state.definition.pages;
      const index = pages.findIndex((page) => page.id === action.pageId);

      if (index < 0 || pages.length === 1) {
        return state;
      }

      const removed = new Set(pages[index]!.questions.map((question) => question.id));
      const neighbour = pages[index + 1] ?? pages[index - 1];

      return {
        ...state,
        // Rules lose their conditions on the page's questions, and jumps to it go (AWK-31).
        definition: {
          ...state.definition,
          pages: withoutConditionsOn(state.definition, [...removed]).pages
            .filter((page) => page.id !== action.pageId)
            .map((page) => {
              if (page.after?.goTo !== action.pageId) {
                return page;
              }

              const { after: _, ...rest } = page;
              return rest as Page;
            }),
        },
        selectedPageId: state.selectedPageId === action.pageId ? (neighbour?.id ?? null) : state.selectedPageId,
        selectedQuestionId: state.selectedQuestionId !== null && removed.has(state.selectedQuestionId) ? null : state.selectedQuestionId,
      };
    }

    case "setLayout":
      return { ...state, definition: { ...state.definition, layout: action.layout } };

    case "setWelcomeEnabled": {
      // Switched on is the default, so it leaves no trace; switched off keeps the texts for later.
      const { enabled: _, ...welcome } = state.definition.welcome ?? {};
      const next = action.enabled ? welcome : { ...welcome, enabled: false };

      return { ...state, definition: withoutEmpty({ ...state.definition, welcome: next }) };
    }

    case "setSurveyText":
      return { ...state, definition: withSurveyText(state.definition, action.field, action.language, action.value) };

    case "setHeaderImage": {
      const { headerImage: _, ...rest } = state.definition;

      return { ...state, definition: action.asset === null ? rest : { ...rest, headerImage: { asset: action.asset } } };
    }

    case "setOptionImage":
      return updateItems(state, action.questionId, "options", (items) =>
        items.map((item, index) => {
          if (index !== action.index) {
            return item;
          }

          const { image: _, ...rest } = item;

          return action.asset === null ? rest : { ...rest, image: { asset: action.asset } };
        }),
      );

    case "setHeaderAlt": {
      const image = state.definition.headerImage;

      if (image === undefined) {
        return state;
      }

      const { alt: _, ...withoutAlt } = image;
      const alt = filled({ ...image.alt, [action.language]: action.value });

      return { ...state, definition: { ...state.definition, headerImage: alt === undefined ? withoutAlt : { ...withoutAlt, alt } } };
    }

    case "setLanguage":
      return { ...state, language: action.language };

    case "addQuestion": {
      const added = { ...newQuestion(action.newId, nextFreeVariable(state.definition), action.questionType), ...action.extra };
      const pages = state.definition.pages.map((page) =>
        page.id === action.pageId ? { ...page, questions: insertAfter(page.questions, action.afterQuestionId, added) } : page,
      );

      return { ...state, definition: { ...state.definition, pages }, selectedQuestionId: added.id };
    }

    case "duplicateQuestion": {
      const original = findQuestion(state.definition, action.questionId);

      if (original === undefined) {
        return state;
      }

      const copy: Question = { ...original, id: action.newId, variable: nextFreeVariable(state.definition) };
      const pages = state.definition.pages.map((page) => ({ ...page, questions: insertAfter(page.questions, original.id, copy, false) }));

      return { ...state, definition: { ...state.definition, pages }, selectedQuestionId: copy.id };
    }

    case "setPageLogic": {
      const pages = state.definition.pages.map((page) => {
        if (page.id !== action.pageId) {
          return page;
        }

        const { showWhen, after, ...rest } = page;
        const nextShow = action.showWhen === undefined ? showWhen : (action.showWhen ?? undefined);
        const nextAfter = action.after === undefined ? after : (action.after ?? undefined);

        return { ...rest, ...(nextShow === undefined ? {} : { showWhen: nextShow }), ...(nextAfter === undefined ? {} : { after: nextAfter }) } as Page;
      });

      return { ...state, definition: { ...state.definition, pages } };
    }

    case "deleteQuestion": {
      const all = allQuestions(state.definition);
      const index = all.findIndex((question) => question.id === action.questionId);
      const neighbour = all[index + 1] ?? all[index - 1];
      const pages = withoutConditionsOn(state.definition, [action.questionId]).pages.map((page) => ({
        ...page,
        questions: page.questions.filter((question) => question.id !== action.questionId),
      }));
      const selectedQuestionId =
        state.selectedQuestionId === action.questionId ? (neighbour?.id ?? null) : state.selectedQuestionId;

      return { ...state, definition: { ...state.definition, pages }, selectedQuestionId };
    }

    case "moveQuestion": {
      const moving = findQuestion(state.definition, action.questionId);

      if (moving === undefined) {
        return state;
      }

      const pages = state.definition.pages
        .map((page) => ({ ...page, questions: page.questions.filter((question) => question.id !== moving.id) }))
        .map((page) =>
          page.id === action.toPageId
            ? { ...page, questions: [...page.questions.slice(0, action.toIndex), moving, ...page.questions.slice(action.toIndex)] }
            : page,
        );

      return { ...state, definition: { ...state.definition, pages } };
    }

    case "setText":
      return updateQuestion(state, action.questionId, (question) => ({
        ...question,
        [action.field]: { ...question[action.field], [action.language]: action.value },
      }));

    case "setVariable":
      return updateQuestion(state, action.questionId, (question) => ({ ...question, variable: action.variable }));

    case "setRequired":
      return updateQuestion(state, action.questionId, (question) => ({ ...question, required: action.required }));

    case "setType":
      return updateQuestion(state, action.questionId, (question) => withType(question, action.questionType));

    case "setItemLabel":
      return updateItems(state, action.questionId, action.list, (items) =>
        items.map((item, index) => (index === action.index ? { ...item, label: { ...item.label, [action.language]: action.value } } : item)),
      );

    case "setItemCode":
      return updateItems(state, action.questionId, action.list, (items) =>
        items.map((item, index) => (index === action.index ? { ...item, code: action.code } : item)),
      );

    case "addItem":
      return updateItems(state, action.questionId, action.list, (items) => [...items, { code: nextFreeCode(items, action.list), label: {} }]);

    case "removeItem":
      return updateItems(state, action.questionId, action.list, (items) => items.filter((_, index) => index !== action.index));

    case "setScale":
      return updateQuestion(state, action.questionId, (question) => ({
        ...question,
        scale: { ...question.scale, min: action.min, max: action.max },
      }));

    case "setScaleLabel":
      return updateQuestion(state, action.questionId, (question) => ({
        ...question,
        scale: {
          min: question.scale?.min ?? 1,
          max: question.scale?.max ?? 5,
          ...question.scale,
          [action.end]: { ...question.scale?.[action.end], [action.language]: action.value },
        },
      }));

    case "setFlag":
      return updateQuestion(state, action.questionId, (question) => ({ ...question, [action.flag]: action.value }));

    case "patch":
      return updateQuestion(state, action.questionId, (question) => {
        const changed: Record<string, unknown> = { ...question, ...action.patch };

        for (const key of action.remove ?? []) {
          delete changed[key];
        }

        return changed as unknown as Question;
      });

    case "addPage": {
      const page: Page = { id: action.newId, questions: [] };

      return { ...state, definition: { ...state.definition, pages: [...state.definition.pages, page] } };
    }
  }
}

export function allQuestions(definition: SurveyDefinition): readonly Question[] {
  return definition.pages.flatMap((page) => page.questions);
}

export function findQuestion(definition: SurveyDefinition, questionId: string | null): Question | undefined {
  return allQuestions(definition).find((question) => question.id === questionId);
}

/** Q1, Q2, … : the first number after the highest one in use, so names never collide. */
function nextFreeVariable(definition: SurveyDefinition): string {
  const used = new Set(allQuestions(definition).map((question) => question.variable));
  let number = allQuestions(definition).length + 1;

  while (used.has(`Q${number}`)) {
    number += 1;
  }

  return `Q${number}`;
}

/** Settings that belong to one group of types only; they go when the type changes to one that cannot use them. */
const typeSettings: readonly [keyof Question, readonly QuestionType[]][] = [
  ["other", ["single", "multiple", "dropdown"]],
  ["order", ["dropdown"]],
  ["display", ["rating"]],
  ["yes", ["yes-no"]],
  ["no", ["yes-no"]],
  ["format", ["short-text"]],
  ["placeholder", ["short-text"]],
  ["mode", ["date"]],
  ["earliest", ["date"]],
  ["latest", ["date"]],
  ["minuteStep", ["date"]],
];

/** Changes the type; keeps text, variable and whatever the new type can use, and adds what it needs. */
function withType(question: Question, type: QuestionType): Question {
  const kept: Record<string, unknown> = { ...question };

  for (const [key, types] of typeSettings) {
    if (!types.includes(type)) {
      delete kept[key];
    }
  }

  const { options, rows, scale, ...rest } = kept as unknown as Question;
  const defaults = newQuestion(question.id, question.variable, type);
  const changed: Question = { ...rest, type, required: type === "text-block" || type === "image" ? false : question.required };

  return {
    ...changed,
    ...(defaults.options !== undefined && { options: options ?? defaults.options }),
    ...(defaults.rows !== undefined && { rows: rows ?? defaults.rows }),
    ...(defaults.scale !== undefined && { scale: scale ?? defaults.scale }),
  };
}

/** Options get the next free number; matrix rows R1, R2, … */
function nextFreeCode(items: readonly AnswerOption[], list: ItemList): string {
  const prefix = list === "rows" ? "R" : "";
  const used = new Set(items.map((item) => item.code));
  let number = items.length + 1;

  while (used.has(`${prefix}${number}`)) {
    number += 1;
  }

  return `${prefix}${number}`;
}

function newQuestion(id: string, variable: string, type: QuestionType): Question {
  const base: Question = { id, variable, type, text: {}, required: false };
  const twoOptions = [
    { code: "1", label: {} },
    { code: "2", label: {} },
  ];

  switch (type) {
    case "single":
    case "multiple":
    case "dropdown":
    case "ranking":
      return { ...base, options: twoOptions };
    case "slider":
      return { ...base, scale: { min: 0, max: 100, step: 1 } };
    case "matrix":
      return { ...base, options: twoOptions, rows: [{ code: "R1", label: {} }] };
    case "rating":
      return { ...base, scale: { min: 1, max: 5 } };
    default:
      return base;
  }
}

/** Inserts after the given question, or at the start when there is none; with atStartIfMissing false, only after it. */
function insertAfter(questions: readonly Question[], afterId: string | null, inserted: Question, atStartIfMissing = true): Question[] {
  const index = questions.findIndex((question) => question.id === afterId);

  if (index < 0) {
    return atStartIfMissing ? [inserted, ...questions] : [...questions];
  }

  return [...questions.slice(0, index + 1), inserted, ...questions.slice(index + 1)];
}

function updateItems(
  state: DesignerState,
  questionId: string,
  list: ItemList,
  change: (items: readonly AnswerOption[]) => AnswerOption[],
): DesignerState {
  return updateQuestion(state, questionId, (question) => ({ ...question, [list]: change(question[list] ?? []) }));
}

function updateQuestion(state: DesignerState, questionId: string, change: (question: Question) => Question): DesignerState {
  const pages = state.definition.pages.map((page) => ({
    ...page,
    questions: page.questions.map((question) => (question.id === questionId ? change(question) : question)),
  }));

  return { ...state, definition: { ...state.definition, pages } };
}

/** Sets one language of a survey text; a text that is blank in every language is removed, so the default applies. */
function withSurveyText(definition: SurveyDefinition, field: SurveyTextField, language: LanguageCode, value: string): SurveyDefinition {
  const set = (text: TranslatedText | undefined) => filled({ ...text, [language]: value });

  if (field === "endText") {
    const { endPage: _, ...rest } = definition;
    const text = set(definition.endPage?.text);

    return text === undefined ? rest : { ...rest, endPage: { text } };
  }

  const key = field === "welcomeTitle" ? "title" : "text";
  const { [key]: _, ...welcome } = definition.welcome ?? {};
  const text = set(definition.welcome?.[key]);

  return withoutEmpty({ ...definition, welcome: text === undefined ? welcome : { ...welcome, [key]: text } });
}

/** The text without blank languages; undefined when nothing is left. */
function filled(text: TranslatedText): TranslatedText | undefined {
  const kept = Object.fromEntries(Object.entries(text).filter(([, value]) => value !== undefined && value !== ""));

  return Object.keys(kept).length > 0 ? kept : undefined;
}

/** Leaves out a welcome page with nothing in it, so the definition stays as small as the survey. */
function withoutEmpty(definition: SurveyDefinition): SurveyDefinition {
  if (definition.welcome !== undefined && Object.keys(definition.welcome).length === 0) {
    const { welcome: _, ...rest } = definition;

    return rest;
  }

  return definition;
}
