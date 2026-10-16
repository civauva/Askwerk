// SPDX-License-Identifier: Apache-2.0
// Checks a theme before it is saved (AWK-17, W10 note 2): contrast after WCAG 2.2, colours, fonts and images.
// Twin of Askwerk.Domain/Themes/ThemeChecks.cs; tests/corpus/theme-checks keeps both in step.
import atelier from "../presets/atelier.json";
import blaupause from "../presets/blaupause.json";
import graphit from "../presets/graphit.json";
import kiosk from "../presets/kiosk.json";
import klar from "../presets/klar.json";
import konfetti from "../presets/konfetti.json";
import kontrast from "../presets/kontrast.json";
import minimal from "../presets/minimal.json";
import nacht from "../presets/nacht.json";
import panorama from "../presets/panorama.json";
import papier from "../presets/papier.json";
import pastell from "../presets/pastell.json";
import sonne from "../presets/sonne.json";
import wald from "../presets/wald.json";
import warm from "../presets/warm.json";
import werkstatt from "../presets/werkstatt.json";
import zeitung from "../presets/zeitung.json";
import type { TranslatedText } from "./definition";
import type { ThemeImage, ThemeTokens } from "./theme";

export interface ThemeIssue {
  readonly code: string;
  readonly severity: "error" | "warning";
  readonly path: string;
  /** Contrast issues: the colour the one at path is read against. */
  readonly against?: string;
  /** Contrast issues: the ratio found, to two decimals. */
  readonly ratio?: number;
}

/** What a look is good for (W26): the gallery filters by it. */
export type ThemeTag = "calm" | "colourful" | "pictures" | "monochrome" | "print" | "dark" | "kiosk";

export interface ThemePreset {
  readonly id: string;
  readonly name: TranslatedText;
  /** One line under the name in the gallery (W26). */
  readonly description?: TranslatedText;
  readonly tags?: readonly ThemeTag[];
  readonly tokens: ThemeTokens;
}

/** Askwerk's looks (W10 A, W26): Werkstatt first, then the rest of the gallery. */
export const themePresets: readonly ThemePreset[] = [werkstatt, klar, warm, kontrast, atelier, konfetti, graphit, papier, nacht, pastell, kiosk, zeitung, blaupause, wald, sonne, minimal, panorama] as ThemePreset[];

type Rgb = readonly [number, number, number];
type ColourName = keyof ThemeTokens["colours"];

const colourNames: readonly ColourName[] = ["accent", "onAccent", "background", "surface", "text", "muted", "line"];

/** Colour pairs that must be readable: [foreground, background, minimum ratio]. 3:1 is for non-text parts (WCAG 1.4.11). */
const pairs: readonly (readonly [ColourName, ColourName, number])[] = [
  ["text", "surface", 4.5],
  ["text", "background", 4.5],
  ["muted", "surface", 4.5],
  ["onAccent", "accent", 4.5],
  ["accent", "surface", 3],
];

const hex = /^#[0-9a-fA-F]{6}$/;

function rgb(colour: string): Rgb {
  return [1, 3, 5].map((start) => parseInt(colour.slice(start, start + 2), 16) / 255) as unknown as Rgb;
}

function luminance([r, g, b]: Rgb): number {
  const linear = (channel: number) => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);

  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

/** The WCAG contrast ratio of two colours. */
export function contrastRatio(first: Rgb, second: Rgb): number {
  const [high, low] = [luminance(first), luminance(second)].sort((a, b) => b - a) as [number, number];

  return (high + 0.05) / (low + 0.05);
}

const round2 = (value: number) => Math.floor(value * 100 + 0.5) / 100;

/** Everything that keeps a theme from being saved (errors) or deserves a look (warnings), in a fixed order. */
function checkColours(colours: ThemeTokens["colours"], path: string, issues: ThemeIssue[]): Map<ColourName, Rgb> {
  const valid = new Map<ColourName, Rgb>();

  for (const name of colourNames) {
    const value = colours[name];

    if (typeof value !== "string" || !hex.test(value)) {
      issues.push({ code: "colour-invalid", severity: "error", path: `${path}/${name}` });
    } else {
      valid.set(name, rgb(value));
    }
  }

  return valid;
}

function checkContrast(valid: Map<ColourName, Rgb>, path: string, issues: ThemeIssue[]) {
  for (const [foreground, background, minimum] of pairs) {
    const front = valid.get(foreground);
    const back = valid.get(background);

    if (front !== undefined && back !== undefined) {
      const ratio = round2(contrastRatio(front, back));

      if (ratio < minimum) {
        issues.push({ code: "contrast-too-low", severity: "error", path: `${path}/${foreground}`, against: `${path}/${background}`, ratio });
      }
    }
  }
}

/** Everything that keeps a theme from being saved (errors) or deserves a look (warnings), in a fixed order. */
export function checkTheme(tokens: ThemeTokens): ThemeIssue[] {
  const issues: ThemeIssue[] = [];
  const valid = checkColours(tokens.colours, "colours", issues);

  for (const font of ["heading", "body"] as const) {
    if ((tokens.fonts[font].family ?? "").trim() === "") {
      issues.push({ code: "font-missing", severity: "error", path: `fonts/${font}` });
    }
  }

  checkContrast(valid, "colours", issues);

  // Dark mode (W26): the dark colours meet the same pairs, and dark or auto needs them.
  if (tokens.darkColours !== undefined) {
    checkContrast(checkColours(tokens.darkColours, "darkColours", issues), "darkColours", issues);
  } else if (tokens.mode === "dark" || tokens.mode === "auto") {
    issues.push({ code: "dark-colours-missing", severity: "error", path: "darkColours" });
  }

  // The error colour (W26) is text on the surface.
  const error = tokens.components?.messages?.error;

  if (error !== undefined) {
    const surface = valid.get("surface");

    if (!hex.test(error)) {
      issues.push({ code: "colour-invalid", severity: "error", path: "components/messages/error" });
    } else if (surface !== undefined) {
      const ratio = round2(contrastRatio(rgb(error), surface));

      if (ratio < 4.5) {
        issues.push({ code: "contrast-too-low", severity: "error", path: "components/messages/error", against: "colours/surface", ratio });
      }
    }
  }

  if (tokens.logo !== undefined) {
    checkImage(tokens.logo, "logo", issues);
  }

  if (tokens.headerImage !== undefined) {
    checkImage(tokens.headerImage, "headerImage", issues);
  }

  const background = tokens.background;

  if (background !== undefined) {
    if (background.kind === "image" || background.kind === "image-overlay") {
      if (background.image === undefined) {
        issues.push({ code: "background-image-missing", severity: "error", path: "background/image" });
      } else {
        checkImage(background.image, "background/image", issues);
      }
    }

    if (background.kind === "image") {
      issues.push({ code: "contrast-not-checked", severity: "warning", path: "background/image" });
    }

    if (background.kind === "image-overlay") {
      const opacity = background.overlayOpacity ?? 1;
      const text = valid.get("text");

      if (!(opacity >= 0 && opacity <= 1)) {
        issues.push({ code: "opacity-out-of-range", severity: "error", path: "background/overlayOpacity" });
      } else if (typeof background.overlay !== "string" || !hex.test(background.overlay)) {
        issues.push({ code: "colour-invalid", severity: "error", path: "background/overlay" });
      } else if (text !== undefined) {
        // The image is unknown, so the overlay is judged over the worst case: a black and a white image.
        const overlay = rgb(background.overlay);
        const over = (base: number) => overlay.map((channel) => opacity * channel + (1 - opacity) * base) as unknown as Rgb;
        const ratio = round2(Math.min(contrastRatio(text, over(0)), contrastRatio(text, over(1))));

        if (ratio < 4.5) {
          issues.push({ code: "contrast-too-low", severity: "error", path: "background/overlay", against: "colours/text", ratio });
        }
      }
    }
  }

  return issues;
}

function checkImage(image: ThemeImage, path: string, issues: ThemeIssue[]) {
  if ((image.asset ?? "").trim() === "") {
    issues.push({ code: "asset-missing", severity: "error", path: `${path}/asset` });
  }

  for (const axis of ["focalX", "focalY"] as const) {
    const value = image[axis];

    if (value !== undefined && !(value >= 0 && value <= 1)) {
      issues.push({ code: "focal-out-of-range", severity: "error", path: `${path}/${axis}` });
    }
  }
}
