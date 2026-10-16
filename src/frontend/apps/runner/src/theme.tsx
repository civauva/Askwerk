// SPDX-License-Identifier: AGPL-3.0-only
// The survey's look in the runner (AWK-17 step 3, W10 E): the theme's tokens become the page's CSS variables and
// data attributes, so runner.css draws every theme with the same rules. Without a theme the Werkstatt look stays.
import type { SurveyDefinition, ThemeColours, ThemeImage, ThemeTokens, TranslatedText } from "@askwerk/schema";
import { useEffect, useState, type CSSProperties } from "react";

export type AssetUrl = (assetId: string, width?: number) => string;

export interface Look {
  readonly style: CSSProperties;
  /** The colours in dark mode (W26); dark: always, auto: when the device prefers dark. */
  readonly dark?: { readonly style: CSSProperties; readonly always: boolean };
  readonly data: Record<`data-${string}`, string>;
  readonly progress: ThemeTokens["progress"];
  /** A pattern drawn from the colours above the content (W26). */
  readonly pattern: NonNullable<ThemeTokens["decoration"]>["pattern"];
  readonly logo: string | undefined;
  readonly hero: { readonly src: string; readonly srcSet: string; readonly position: string; readonly alt?: TranslatedText } | undefined;
  readonly fonts: readonly { readonly family: string; readonly url: string }[];
}

const radii: Record<ThemeTokens["corners"], [string, string]> = { sharp: ["2px", "4px"], rounded: ["8px", "12px"], pill: ["999px", "16px"] };

const stack = (family: string) => `"${family.replace(/"/g, "")}", "IBM Plex Sans", "Segoe UI", system-ui, sans-serif`;

const position = (image: ThemeImage) => `${Math.round((image.focalX ?? 0.5) * 100)}% ${Math.round((image.focalY ?? 0.5) * 100)}%`;

function rgba(hex: string, opacity: number): string {
  const [r, g, b] = [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16));

  return `rgba(${r}, ${g}, ${b}, ${opacity})`;
}

const heroOf = (asset: string, assetUrl: AssetUrl) => ({
  src: assetUrl(asset, 1600),
  srcSet: [960, 1600, 2400].map((width) => `${assetUrl(asset, width)} ${width}w`).join(", "),
});

/** The colours as the page's CSS variables. */
function colourVariables(colours: ThemeColours): Record<string, string> {
  return {
    "--aw-signal": colours.accent,
    "--aw-on-signal": colours.onAccent,
    "--aw-focus": colours.accent,
    "--aw-paper": colours.background,
    "--aw-surface": colours.surface,
    "--aw-ink": colours.text,
    "--aw-graphite": `color-mix(in srgb, ${colours.text} 80%, ${colours.surface})`,
    "--aw-muted": colours.muted,
    "--aw-line": colours.line,
    "--aw-line-strong": `color-mix(in srgb, ${colours.line} 70%, ${colours.text})`,
    "--aw-signal-soft": `color-mix(in srgb, ${colours.accent} 14%, ${colours.surface})`,
    "--aw-signal-ink": `color-mix(in srgb, ${colours.accent} 70%, ${colours.text})`,
  };
}

const flag = (value: boolean | undefined, on: string, off: string) => (value === undefined ? undefined : value ? on : off);

/** The finer settings (W26) as data attributes; unset ones are left out, so runner.css keeps the main settings' look. */
function partAttributes(theme: ThemeTokens): Record<`data-${string}`, string> {
  const { components: parts = {}, pages = {} } = theme;
  const all: Record<`data-${string}`, string | undefined> = {
    "data-q-size": parts.question?.size,
    "data-q-font": parts.question?.font,
    "data-numbered": flag(parts.question?.numbered, "on", "off"),
    "data-columns": parts.choices?.columns ?? (theme.controls === "tiles" ? "two" : undefined),
    "data-selected": parts.choices?.selected,
    "data-marker": parts.choices?.marker,
    "data-other": parts.choices?.other,
    "data-button-width": parts.buttons?.width,
    "data-button-align": parts.buttons?.align,
    "data-fields": parts.textFields?.style ?? (theme.controls === "underline" ? "underline" : undefined),
    "data-rating-size": parts.rating?.size,
    "data-rating-selected": parts.rating?.selected,
    "data-slider-track": parts.slider?.track,
    "data-slider-thumb": parts.slider?.thumb,
    "data-matrix-stripes": flag(parts.matrix?.stripes, "on", "off"),
    "data-matrix-header": parts.matrix?.header,
    "data-progress-label": flag(parts.progress?.label, "on", "off"),
    "data-progress-position": parts.progress?.position,
    "data-messages": parts.messages?.style,
    "data-header": flag(pages.header?.show, "on", "off"),
    "data-org": flag(pages.header?.organisation, "on", "off"),
    "data-logo-size": pages.header?.logoSize,
    "data-welcome": pages.welcome?.layout,
    "data-end": pages.end?.layout,
    "data-width": pages.questions?.width,
    "data-cards": flag(pages.questions?.cards, "on", "off"),
    "data-pattern": theme.decoration?.pattern === "none" ? undefined : theme.decoration?.pattern,
    "data-panel": pages.questions?.panel,
    "data-title": pages.title,
    "data-text-block": parts.textBlock?.style,
  };

  return Object.fromEntries(Object.entries(all).filter((entry): entry is [`data-${string}`, string] => entry[1] !== undefined));
}

/** Everything the page needs from the theme, with file addresses for this survey; the survey's own header image wins. */
export function themeLook(theme: ThemeTokens, assetUrl: AssetUrl, own?: SurveyDefinition["headerImage"]): Look {
  const { colours } = theme;
  const [control, card] = radii[theme.corners] ?? radii.sharp;
  const background = theme.background;
  const style: Record<string, string> = {
    ...colourVariables(colours),
    "--aw-font": stack(theme.fonts.body.family),
    "--aw-font-display": stack(theme.fonts.heading.family),
    "--run-radius": control,
    "--run-radius-card": card,
  };

  if (theme.components?.messages?.error !== undefined) {
    style["--aw-error"] = theme.components.messages.error;
  }

  if (background?.kind === "image-overlay" && background.image !== undefined && background.overlay !== undefined) {
    const veil = rgba(background.overlay, background.overlayOpacity ?? 1);
    style.backgroundImage = `linear-gradient(${veil}, ${veil}), url("${assetUrl(background.image.asset, 1600)}")`;
    style.backgroundSize = background.fit === "tile" ? "auto" : background.fit === "contain" ? "contain" : "cover";
    style.backgroundRepeat = background.fit === "tile" ? "repeat" : "no-repeat";
    style.backgroundPosition = position(background.image);
    style.backgroundAttachment = "fixed";
  } else if (background?.kind === "image" && background.image !== undefined) {
    style.backgroundImage = `url("${assetUrl(background.image.asset, 1600)}")`;
    style.backgroundSize = background.fit === "tile" ? "auto" : background.fit === "contain" ? "contain" : "cover";
    style.backgroundPosition = position(background.image);
  }

  const header = theme.headerImage;
  const showsHero = header !== undefined && (theme.layout === "header-image" || theme.layout === "split-image");

  const dark =
    theme.darkColours !== undefined && (theme.mode === "dark" || theme.mode === "auto") ? { style: colourVariables(theme.darkColours) as CSSProperties, always: theme.mode === "dark" } : undefined;

  return {
    style: style as CSSProperties,
    ...(dark !== undefined && { dark }),
    pattern: theme.decoration?.pattern ?? "none",
    data: {
      ...partAttributes(theme),
      "data-layout": theme.layout,
      "data-corners": theme.corners,
      "data-controls": theme.controls,
      "data-buttons": theme.buttons,
      "data-progress": theme.progress,
      "data-scale": theme.scale,
      "data-spacing": theme.spacing,
    },
    progress: theme.progress,
    logo: theme.logo === undefined ? undefined : assetUrl(theme.logo.asset, 480),
    hero:
      own !== undefined
        ? { ...heroOf(own.asset, assetUrl), position: "50% 50%", ...(own.alt !== undefined && { alt: own.alt }) }
        : showsHero
          ? { ...heroOf(header.asset, assetUrl), position: position(header) }
          : undefined,
    fonts: [theme.fonts.heading, theme.fonts.body].flatMap((font) => (font.asset === undefined ? [] : [{ family: font.family, url: assetUrl(font.asset) }])),
  };
}

/** Uploaded fonts as @font-face rules in the document head, removed again when the look goes. */
export function ThemeFonts({ look }: { look: Look }) {
  const rules = look.fonts.map((font) => `@font-face { font-family: "${font.family.replace(/"/g, "")}"; src: url("${font.url}"); font-display: swap; }`).join("\n");

  useEffect(() => {
    if (rules === "") {
      return undefined;
    }

    const style = document.createElement("style");
    style.dataset.runTheme = "fonts";
    style.textContent = rules;
    document.head.append(style);

    return () => style.remove();
  }, [rules]);

  return null;
}

/** Whether the device prefers dark colours, kept up to date (W26). */
export function usePrefersDark(): boolean {
  const [query] = useState(() => (typeof window === "undefined" || typeof window.matchMedia !== "function" ? null : window.matchMedia("(prefers-color-scheme: dark)")));
  const [dark, setDark] = useState(query?.matches ?? false);

  useEffect(() => {
    if (query === null) {
      return undefined;
    }

    const changed = (event: MediaQueryListEvent) => setDark(event.matches);
    query.addEventListener?.("change", changed);

    return () => query.removeEventListener?.("change", changed);
  }, [query]);

  return dark;
}
