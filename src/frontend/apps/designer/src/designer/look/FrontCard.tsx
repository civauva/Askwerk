// SPDX-License-Identifier: AGPL-3.0-only
import type { ThemeTokens } from "@askwerk/schema";
import type { CSSProperties } from "react";
import "./look.css";

const control = { sharp: "2px", rounded: "6px", pill: "999px" } as const;
const card = { sharp: "2px", rounded: "8px", pill: "14px" } as const;
/** An uploaded image in the size a card needs. */
export const frontAssetUrl = (assetId: string, width: number) => `/api/app/assets/${encodeURIComponent(assetId)}?w=${width}`;
const stack = (family: string) => `"${family.replace(/"/g, "")}", "IBM Plex Sans", sans-serif`;

type FrontCardProps = {
  tokens: ThemeTokens;
  title: string;
  text: string;
  organisation: string;
  start: string;
  /** The survey's own header picture, else the look's (as the runner chooses). */
  headerAsset?: string | null;
  /** Where an uploaded image is shown, in a small size. */
  assetUrl?: (assetId: string, width: number) => string;
};

/**
 * A survey's front page in miniature (W32), as the runner draws it: the page in the look's colours with its pattern,
 * scene or header picture, the organisation's bar with the logo, and the welcome card (left, centred or on an accent
 * band; solid or frosted) with the title, text and Start in the look's button style. From the tokens, not a
 * screenshot, so a list of fifty stays light.
 */
export function FrontCard({ tokens, title, text, organisation, start, headerAsset = null, assetUrl }: FrontCardProps) {
  const colours = tokens.mode === "dark" && tokens.darkColours !== undefined ? tokens.darkColours : tokens.colours;
  const pattern = tokens.decoration?.pattern ?? "none";
  const scene = pattern === "mountains";
  const picture = headerAsset ?? (tokens.layout === "header-image" || tokens.layout === "split-image" ? (tokens.headerImage?.asset ?? null) : null);
  const logo = tokens.logo?.asset;
  const header = tokens.pages?.header;
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
    "--lt-radius": control[tokens.corners],
    "--lt-card": card[tokens.corners],
  } as CSSProperties;

  return (
    <div
      className="lt fc"
      style={style}
      data-scene={scene ? "mountains" : undefined}
      data-welcome={tokens.pages?.welcome?.layout ?? "left"}
      data-panel={tokens.pages?.questions?.panel}
      data-buttons={tokens.buttons}
      data-picture={picture !== null && assetUrl !== undefined ? "" : undefined}
      aria-hidden="true"
    >
      {pattern !== "none" && !scene && <div className={`lt-pattern lt-pattern--${pattern} fc-band`} />}
      {picture !== null && assetUrl !== undefined && <div className="fc-picture" style={{ backgroundImage: `url("${assetUrl(picture, 480)}")` }} />}
      {header?.show !== false && (
        <div className="fc-head">
          {logo !== undefined && assetUrl !== undefined ? <img src={assetUrl(logo, 160)} alt="" /> : <i>{organisation.slice(0, 1).toUpperCase()}</i>}
          {header?.organisation !== false && <span>{organisation}</span>}
        </div>
      )}
      <div className="fc-card">
        <div className="fc-title">{title}</div>
        {text !== "" && <div className="fc-lead">{text}</div>}
        <span className="fc-go">{start}</span>
      </div>
    </div>
  );
}

/** The look as a strip (W32 card A): the page's colours, pattern, scene or picture, the organisation, Start. */
export function LookStrip({ tokens, organisation, start, headerAsset = null, assetUrl }: Omit<FrontCardProps, "title" | "text">) {
  const colours = tokens.mode === "dark" && tokens.darkColours !== undefined ? tokens.darkColours : tokens.colours;
  const pattern = tokens.decoration?.pattern ?? "none";
  const scene = pattern === "mountains";
  const picture = headerAsset ?? (tokens.layout === "header-image" || tokens.layout === "split-image" ? (tokens.headerImage?.asset ?? null) : null);
  const style = {
    "--lt-bg": colours.background,
    "--lt-surface": colours.surface,
    "--lt-text": colours.text,
    "--lt-accent": colours.accent,
    "--lt-on": colours.onAccent,
    "--lt-body": stack(tokens.fonts.body.family),
    "--lt-radius": control[tokens.corners],
  } as CSSProperties;

  return (
    <div className="lt fs" style={style} data-scene={scene ? "mountains" : undefined} data-buttons={tokens.buttons} data-picture={picture !== null && assetUrl !== undefined ? "" : undefined} aria-hidden="true">
      {pattern !== "none" && !scene && <div className={`lt-pattern lt-pattern--${pattern} fs-band`} />}
      {picture !== null && assetUrl !== undefined && <div className="fs-picture" style={{ backgroundImage: `url("${assetUrl(picture, 480)}")` }} />}
      <span className="fs-org">● {organisation}</span>
      <span className="fc-go fs-go">{start}</span>
    </div>
  );
}
