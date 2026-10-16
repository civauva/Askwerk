// SPDX-License-Identifier: AGPL-3.0-only
import type { LanguageCode, SurveyDefinition, Translate, TranslatedText } from "@askwerk/schema";
import type { Dispatch } from "react";
import type { DesignerMessageKey } from "./messages";
import type { DesignerAction, DesignerState } from "./state";

/** Props every designer region shares. */
export interface RegionProps {
  t: Translate<DesignerMessageKey>;
  state: DesignerState;
  dispatch: Dispatch<DesignerAction>;
}

/** The text in the given language, or undefined when it is missing or blank. */
export function textIn(text: TranslatedText | undefined, language: LanguageCode): string | undefined {
  const value = text?.[language];

  return value !== undefined && value.trim() !== "" ? value : undefined;
}

/** What the designer shows for a text: the editing language first, then the default language. */
export function displayText(text: TranslatedText | undefined, language: LanguageCode, definition: SurveyDefinition): string | undefined {
  return textIn(text, language) ?? textIn(text, definition.defaultLanguage);
}

export function languageName(t: Translate<DesignerMessageKey>, language: LanguageCode): string {
  if (language === "de") {
    return t("languageDe");
  }

  return language === "en" ? t("languageEn") : language.toUpperCase();
}
