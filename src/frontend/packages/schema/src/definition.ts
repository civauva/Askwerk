// SPDX-License-Identifier: Apache-2.0
// The survey definition: the full schema of one survey version (agents/data-model.md, Survey definition).

/** A language code of a survey, e.g. "de" or "en". Surveys may use more languages than the product UI. */
export type LanguageCode = string;

/** All human-readable text is a translation map, never a plain string (data-model.md, rule 1). */
export type TranslatedText = Readonly<Record<LanguageCode, string>>;

export type QuestionType =
  | "single"
  | "multiple"
  | "dropdown"
  | "yes-no"
  | "slider"
  | "rating"
  | "nps"
  | "short-text"
  | "long-text"
  | "number"
  | "date"
  | "matrix"
  | "ranking"
  | "text-block"
  | "image";

/** An uploaded image in a survey (AWK-17, W10 F1): the alt text says what it shows, per language. */
export interface QuestionImage {
  readonly asset: string;
  readonly alt?: TranslatedText;
  /** Image blocks: how wide; full when not given. */
  readonly size?: "full" | "wide" | "half" | "small";
  /** Image blocks: where a narrower image sits; centre when not given. */
  readonly align?: "left" | "centre" | "right";
}

export interface AnswerOption {
  readonly code: string;
  readonly label: TranslatedText;
  /** Single and multiple choice: the option as a picture; its label is the alt text (AWK-17, W10 F1). */
  readonly image?: { readonly asset: string };
}

export interface Question {
  readonly id: string;
  readonly variable: string;
  readonly type: QuestionType;
  readonly text: TranslatedText;
  readonly help?: TranslatedText;
  readonly required: boolean;
  /** Image blocks: the image; other questions: an image shown with the question (AWK-17, W10 F1). */
  readonly image?: QuestionImage;
  /** Show the options in a random order per respondent. */
  readonly randomize?: boolean;
  /** Offer a "Don't know" answer. */
  readonly dontKnow?: boolean;
  readonly options?: readonly AnswerOption[];
  readonly rows?: readonly AnswerOption[];
  readonly scale?: {
    readonly min: number;
    readonly max: number;
    readonly minLabel?: TranslatedText;
    readonly maxLabel?: TranslatedText;
    /** Slider only: the distance between values; it divides max − min. */
    readonly step?: number;
    /** Slider only: show the value above the handle. */
    readonly showValue?: boolean;
  };
  /** Rating only: how the steps look; the stored value is the same (W7, panel 7). */
  readonly display?: "numbers" | "stars" | "dots" | "faces";
  /** Dropdown only: the order the options are offered in. */
  readonly order?: "entered" | "alphabetical";
  /** Single, multiple and dropdown: an extra last option with a text field; the text goes into its own variable. */
  readonly other?: { readonly code: string; readonly label: TranslatedText; readonly variable: string };
  /** Yes/no: labels and codes; Yes/1 and No/0 when not given. */
  readonly yes?: { readonly code: string; readonly label?: TranslatedText };
  readonly no?: { readonly code: string; readonly label?: TranslatedText };
  /** Short text: the format it is checked against. */
  readonly format?: "email" | "phone" | "url";
  readonly placeholder?: TranslatedText;
  /** Date: what is asked for. earliest and latest are written for the mode ("2026-10-14", "2026-10-14T09:30", "09:30"); dates may also be "today". */
  readonly mode?: "date" | "date-time" | "time";
  readonly earliest?: string;
  readonly latest?: string;
  /** Date-time and time: minutes between the offered times; divides 60. */
  readonly minuteStep?: number;
  /** Shown only when these hold (AWK-31); always when not given. Conditions use earlier questions only. */
  readonly showWhen?: LogicGroup;
}

/** What a condition says about an earlier answer (AWK-31, W19 E). */
export type LogicOperator =
  | "is"
  | "is-not"
  | "is-one-of"
  | "includes"
  | "includes-any"
  | "includes-none"
  | "at-least"
  | "at-most"
  | "between"
  | "answered"
  | "not-answered";

/** One condition on an earlier question: an option code (value), codes (values), or a number (number, and to for between). */
export interface LogicCondition {
  readonly question: string;
  readonly op: LogicOperator;
  readonly value?: string;
  readonly values?: readonly string[];
  readonly number?: number;
  readonly to?: number;
}

/** Conditions joined by all (and) or any (or); one level, no nesting (owner decision, W19). */
export interface LogicGroup {
  readonly match: "all" | "any";
  readonly conditions: readonly LogicCondition[];
}

/** After a page (W19 B): when the conditions hold (always without when), jump forward to a page or end the survey. */
export interface PageAfter {
  readonly when?: LogicGroup;
  readonly goTo?: string;
  readonly end?: boolean;
}

export interface Page {
  readonly id: string;
  readonly title?: TranslatedText;
  readonly questions: readonly Question[];
  /** Shown only when these hold (AWK-31); always when not given. */
  readonly showWhen?: LogicGroup;
  /** Jump forward or end after this page (AWK-31); the next page when not given. */
  readonly after?: PageAfter;
}

export interface SurveyDefinition {
  readonly schemaVersion: 1;
  readonly languages: readonly LanguageCode[];
  readonly defaultLanguage: LanguageCode;
  readonly pages: readonly Page[];
  readonly endPage?: { readonly text: TranslatedText };
  /** How the runner shows the questions (W8); classic when not given. */
  readonly layout?: "classic" | "one-at-a-time";
  /** The welcome page before the first question (W8 rev. 2); skipped when a respondent comes back. enabled false switches it off and keeps the texts (W9). */
  readonly welcome?: { readonly title?: TranslatedText; readonly text?: TranslatedText; readonly enabled?: boolean };
  /** The survey's own header image (AWK-17, W10 B); it replaces the theme's. The alt text is required, per language. */
  readonly headerImage?: { readonly asset: string; readonly alt?: TranslatedText };
}
