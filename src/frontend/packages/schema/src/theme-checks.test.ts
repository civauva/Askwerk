// SPDX-License-Identifier: Apache-2.0
import { describe, expect, it } from "vitest";
import type { ThemeTokens } from "./theme";
import { checkTheme, themePresets, type ThemeIssue } from "./theme-checks";

interface CorpusCase {
  readonly name: string;
  readonly tokens: ThemeTokens;
  readonly expected: ThemeIssue[];
}

const corpus = Object.values(import.meta.glob<CorpusCase>("../tests/corpus/theme-checks/*.json", { eager: true, import: "default" }));

describe("checkTheme (shared golden corpus)", () => {
  it.each(corpus.map((c) => [c.name, c] as const))("%s", (_name, c) => {
    expect(checkTheme(c.tokens)).toEqual(c.expected);
  });
});

describe("themePresets", () => {
  it("offers seventeen looks, Werkstatt first, each with a description and tags and passing its own checks (W26)", () => {
    expect(themePresets.map((preset) => preset.id)).toEqual([
      "werkstatt", "klar", "warm", "kontrast", "atelier", "konfetti", "graphit", "papier", "nacht", "pastell", "kiosk", "zeitung", "blaupause", "wald", "sonne", "minimal", "panorama",
    ]);

    for (const preset of themePresets) {
      expect(checkTheme(preset.tokens)).toEqual([]);
      expect(preset.description?.en).toBeTruthy();
      expect(preset.tags?.length).toBeGreaterThan(0);
    }
  });
});
