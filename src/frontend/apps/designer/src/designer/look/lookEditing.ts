// SPDX-License-Identifier: AGPL-3.0-only
// Editing a look as data (W26): read and write one setting by its path, count what differs from the look it started
// from, and make dark colours that pass the same checks.
import { contrastRatio, themePresets, type ThemeColours, type ThemePreset, type ThemeTokens } from "@askwerk/schema";

export type LookPath = readonly string[];

/** The value at the path, or undefined. */
export function getAt(tokens: unknown, path: LookPath): unknown {
  return path.reduce<unknown>((value, key) => (value !== null && typeof value === "object" ? (value as Record<string, unknown>)[key] : undefined), tokens);
}

/** A copy with the value at the path; undefined removes it, and objects left empty go too. */
export function setAt<T>(tokens: T, path: LookPath, value: unknown): T {
  const [key, ...rest] = path;

  if (key === undefined) {
    return value as T;
  }

  const source = (tokens ?? {}) as Record<string, unknown>;
  const next = rest.length === 0 ? value : setAt(source[key], rest, value);
  const copy: Record<string, unknown> = { ...source };

  if (next === undefined || (next !== null && typeof next === "object" && !Array.isArray(next) && Object.keys(next).length === 0)) {
    delete copy[key];
  } else {
    copy[key] = next;
  }

  return copy as T;
}

function leaves(value: unknown, prefix = ""): Map<string, string> {
  const found = new Map<string, string>();

  if (value !== null && typeof value === "object" && !Array.isArray(value)) {
    for (const [key, inner] of Object.entries(value)) {
      for (const [path, leaf] of leaves(inner, `${prefix}/${key}`)) {
        found.set(path, leaf);
      }
    }
  } else if (value !== undefined) {
    found.set(prefix, JSON.stringify(value));
  }

  return found;
}

/** How many settings differ between two looks. */
export function changesBetween(base: ThemeTokens, tokens: ThemeTokens): number {
  const before = leaves(base);
  const after = leaves(tokens);
  const paths = new Set([...before.keys(), ...after.keys()]);

  return [...paths].filter((path) => before.get(path) !== after.get(path)).length;
}

/** A copy of the look with the settings under these paths as in the base: "back to the look's choices". */
export function resetPaths(tokens: ThemeTokens, base: ThemeTokens, paths: readonly LookPath[]): ThemeTokens {
  return paths.reduce((current, path) => setAt(current, path, getAt(base, path)), tokens);
}

/** The preset a look started from, by "preset:{id}". */
export function presetOf(lookBase: string | null | undefined): ThemePreset | undefined {
  return lookBase?.startsWith("preset:") ? themePresets.find((preset) => `preset:${preset.id}` === lookBase) : undefined;
}

type Rgb = readonly [number, number, number];
const rgb = (hex: string): Rgb => [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16) / 255) as unknown as Rgb;
const hex = (colour: Rgb) => `#${colour.map((channel) => Math.round(Math.min(1, Math.max(0, channel)) * 255).toString(16).padStart(2, "0")).join("")}`;
const mix = (from: string, to: string, share: number) => hex(rgb(from).map((channel, index) => channel + (rgb(to)[index]! - channel) * share) as unknown as Rgb);
const ratio = (a: string, b: string) => contrastRatio(rgb(a), rgb(b));

/** Dark colours for a look (W26): a deep background, light text, and the accent lightened until it reads on it. */
export function darkColoursFor(colours: ThemeColours): ThemeColours {
  const background = mix("#11151b", colours.accent, 0.06);
  const surface = mix(background, "#ffffff", 0.05);
  let accent = colours.accent;

  for (let step = 0; step < 20 && ratio(accent, surface) < 4.5; step++) {
    accent = mix(accent, "#ffffff", 0.12);
  }

  return {
    accent,
    onAccent: ratio("#11151b", accent) >= ratio("#ffffff", accent) ? "#11151b" : "#ffffff",
    background,
    surface,
    text: "#eef1f5",
    muted: "#a9b2bf",
    line: mix(surface, "#ffffff", 0.16),
  };
}
