// SPDX-License-Identifier: AGPL-3.0-only
import type { ThemeTokens } from "@askwerk/schema";
import type { CSSProperties } from "react";
import "./look.css";

const radius = { sharp: "2px", rounded: "8px", pill: "999px" } as const;
const card = { sharp: "2px", rounded: "12px", pill: "20px" } as const;
const stack = (family: string) => `"${family.replace(/"/g, "")}", "IBM Plex Sans", sans-serif`;

/**
 * A survey question drawn in a look (W26), from its tokens only: no runner, so a gallery of sixteen stays light. The
 * runner draws the real survey in Preview.
 */
export function LookThumb({ tokens, question, choices, organisation = "Falke & Co" }: { tokens: ThemeTokens; question: string; choices: readonly string[]; organisation?: string }) {
  const colours = tokens.mode === "dark" && tokens.darkColours !== undefined ? tokens.darkColours : tokens.colours;
  const controls = tokens.controls;
  const style = {
    "--lt-bg": colours.background,
    "--lt-surface": colours.surface,
    "--lt-text": colours.text,
    "--lt-muted": colours.muted,
    "--lt-line": colours.line,
    "--lt-accent": colours.accent,
    "--lt-on": colours.onAccent,
    "--lt-head": stack(tokens.fonts.heading.family),
    "--lt-body": stack(tokens.fonts.body.family),
    "--lt-radius": controls === "pills" ? "999px" : controls === "tiles" ? card[tokens.corners] : radius[tokens.corners],
    "--lt-card": card[tokens.corners],
  } as CSSProperties;
  const selected = tokens.components?.choices?.selected ?? "text";
  const pattern = tokens.decoration?.pattern ?? "none";
  const scene = pattern === "mountains";
  const steps = tokens.progress === "steps";

  return (
    <div className="lt" style={style} data-scene={scene ? "mountains" : undefined} data-panel={tokens.pages?.questions?.panel} data-layout={tokens.layout} data-selected={selected} data-controls={controls} data-buttons={tokens.buttons} data-columns={tokens.components?.choices?.columns ?? (controls === "tiles" ? "two" : "one")} aria-hidden="true">
      {pattern !== "none" && !scene && <div className={`lt-pattern lt-pattern--${pattern}`} />}
      <div className="lt-org">● {organisation}</div>
      <div className="lt-card">
        {tokens.progress !== "none" &&
          (steps ? (
            <div className="lt-steps">
              {[0, 1, 2, 3, 4, 5].map((step) => (
                <i key={step} className={step < 2 ? "lt-on" : undefined} />
              ))}
            </div>
          ) : (
            <div className="lt-bar">
              <i />
            </div>
          ))}
        <div className="lt-q">{question}</div>
        <div className="lt-choices">
          {choices.slice(0, 4).map((choice, index) => (
            <div key={`${index}-${choice}`} className={index === 1 ? "lt-choice lt-choice--on" : "lt-choice"}>
              {tokens.components?.choices?.marker !== "none" && <i />}
              {choice}
            </div>
          ))}
        </div>
        <div className="lt-foot">
          <span>←</span>
          <span className="lt-button">→</span>
        </div>
      </div>
    </div>
  );
}
