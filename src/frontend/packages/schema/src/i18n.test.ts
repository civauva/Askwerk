// SPDX-License-Identifier: Apache-2.0
import { describe, expect, it } from "vitest";
import { createTranslator, pickLocale } from "./i18n";

const en = { greeting: "Hello {name}", started: "Started {minutes} min ago" } as const;
const de: Record<keyof typeof en, string> = { greeting: "Hallo {name}", started: "Gestartet vor {minutes} Min." };

describe("createTranslator", () => {
  it("returns the text in the chosen language with parameters filled in", () => {
    const t = createTranslator({ de, en }, "de");

    expect(t("greeting", { name: "Dražan" })).toBe("Hallo Dražan");
  });

  it("formats numbers for the chosen language", () => {
    const t = createTranslator({ de, en }, "de");

    expect(t("started", { minutes: 1.5 })).toBe("Gestartet vor 1,5 Min.");
  });

  it("leaves an unknown parameter visible instead of hiding it", () => {
    const t = createTranslator({ de, en }, "en");

    expect(t("greeting", {})).toBe("Hello {name}");
  });
});

describe("pickLocale", () => {
  it("picks German for any German browser language", () => {
    expect(pickLocale(["de-AT", "en"])).toBe("de");
  });

  it("picks English for everything else", () => {
    expect(pickLocale(["fr-FR"])).toBe("en");
  });

  it("follows the first supported language in the browser's order", () => {
    expect(pickLocale(["en-GB", "de-DE"])).toBe("en");
  });
});
