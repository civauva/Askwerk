// SPDX-License-Identifier: AGPL-3.0-only
import type { ThemeTokens } from "@askwerk/schema";
import type { CSSProperties } from "react";
import "./look.css";

const radius = { sharp: "2px", rounded: "8px", pill: "999px" } as const;
const stack = (family: string) => `"${family.replace(/"/g, "")}", "IBM Plex Sans", sans-serif`;

/**
 * A survey's front page drawn in its look (W32): the welcome page's title and text over the look's background, with
 * its accent on the start button. From the tokens only, not a screenshot, so a list of fifty stays light.
 */
export function FrontCard({ tokens, title, text, organisation, start }: { tokens: ThemeTokens; title: string; text: string; organisation: string; start: string }) {
  const colours = tokens.mode === "dark" && tokens.darkColours !== undefined ? tokens.darkColours : tokens.colours;
  const pattern = tokens.decoration?.pattern ?? "none";
  const scene = pattern === "mountains";
  const style = {
    "--lt-bg": colours.background,
    "--lt-surface": colours.surface,
    "--lt-text": colours.text,
    "--lt-muted": colours.muted,
    "--lt-accent": colours.accent,
    "--lt-on": colours.onAccent,
    "--lt-head": stack(tokens.fonts.heading.family),
    "--lt-body": stack(tokens.fonts.body.family),
    "--lt-radius": radius[tokens.corners],
  } as CSSProperties;

  return (
    <div className="lt fc" style={style} data-scene={scene ? "mountains" : undefined} aria-hidden="true">
      {pattern !== "none" && !scene && <div className={`lt-pattern lt-pattern--${pattern} fc-band`} />}
      <div className="fc-org">● {organisation}</div>
      <div className="fc-title">{title}</div>
      {text !== "" && <div className="fc-lead">{text}</div>}
      <span className="fc-go">{start}</span>
    </div>
  );
}
