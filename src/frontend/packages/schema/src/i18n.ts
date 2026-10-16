// SPDX-License-Identifier: Apache-2.0

/** The product languages (agents/AGENTS.md: German and English from day one). */
export type Locale = "de" | "en";

/** Catalogues for both languages with the same keys; a missing key in either one is a compile error. */
export type Catalogs<Key extends string> = Record<Locale, Record<Key, string>>;

export type Translate<Key extends string> = (key: Key, params?: Record<string, string | number>) => string;

/**
 * Our own i18n layer on the browser's Intl APIs (decisions.md: no i18n framework). Parameters are written
 * as {name}; numbers are formatted for the language.
 */
export function createTranslator<Key extends string>(catalogs: Catalogs<Key>, locale: Locale): Translate<Key> {
  const numbers = new Intl.NumberFormat(locale);

  return (key, params = {}) =>
    catalogs[locale][key].replace(/\{(\w+)\}/g, (placeholder, name: string) => {
      const value = params[name];

      if (value === undefined) {
        return placeholder;
      }

      return typeof value === "number" ? numbers.format(value) : value;
    });
}

/** German for any German browser language that comes first, otherwise English. */
export function pickLocale(languages: readonly string[]): Locale {
  for (const language of languages) {
    const primary = language.toLowerCase().split("-")[0];

    if (primary === "de" || primary === "en") {
      return primary;
    }
  }

  return "en";
}
