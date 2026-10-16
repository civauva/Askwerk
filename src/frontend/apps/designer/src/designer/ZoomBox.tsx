// SPDX-License-Identifier: AGPL-3.0-only
import { useState, type ReactNode } from "react";

const zooms = [0.5, 0.67, 0.75, 0.9, 1, 1.25, 1.5];

/**
 * A preview that fills its place and zooms (W33): smaller to see a whole page, larger to check details; Fit returns
 * to 100 %. Used where a page shows a look or an email beside its settings.
 */
export function ZoomBox({ texts, children }: { texts: { zoom: string; zoomIn: string; zoomOut: string; fit: string }; children: ReactNode }) {
  const [zoom, setZoom] = useState(1);
  const step = (direction: 1 | -1) => setZoom((current) => zooms[Math.max(0, Math.min(zooms.length - 1, zooms.indexOf(current) + direction))] ?? 1);

  return (
    <div className="zoom-box">
      {/* Scrolls when zoomed in, so it takes the keyboard focus too. */}
      <div className="zoom-box__stage" role="region" aria-label={texts.zoom} tabIndex={0}>
        <div className="zoom-box__content" style={zoom === 1 ? undefined : { transform: `scale(${zoom})`, width: `${100 / zoom}%`, height: `${100 / zoom}%` }}>
          {children}
        </div>
      </div>
      <span className="zoom-box__controls" role="group" aria-label={texts.zoom}>
        <button type="button" aria-label={texts.zoomOut} disabled={zoom === zooms[0]} onClick={() => step(-1)}>
          −
        </button>
        <output aria-live="polite">{Math.round(zoom * 100)} %</output>
        <button type="button" aria-label={texts.zoomIn} disabled={zoom === zooms.at(-1)} onClick={() => step(1)}>
          +
        </button>
        <button type="button" onClick={() => setZoom(1)}>
          {texts.fit}
        </button>
      </span>
    </div>
  );
}
