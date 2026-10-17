// SPDX-License-Identifier: AGPL-3.0-only
import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import "./stage.css";

/** The steps of − and +; Ctrl + scroll zooms in between. */
const levels = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.25, 1.5, 2];
const smallest = levels[0]!;
const largest = levels.at(-1)!;
const clamp = (zoom: number) => Math.min(largest, Math.max(smallest, Math.round(zoom * 100) / 100));
/** The space around the page on the stage (24 px each side). */
const gutter = 48;

export type Device = "phone" | "tablet" | "desktop";

/** The page width of each device; the desktop fills the stage. */
export const deviceWidth = (device: Device): number | null => (device === "phone" ? 390 : device === "tablet" ? 768 : null);

/** Phone, Tablet, Desktop (W32): one segmented control, the pressed one is the width shown. */
export function DeviceSwitch({ label, texts, value, onChange }: { label: string; texts: Record<Device, string>; value: Device; onChange: (device: Device) => void }) {
  return (
    <span className="aw-stage__seg" role="group" aria-label={label}>
      {(["phone", "tablet", "desktop"] as const).map((device) => (
        <button key={device} type="button" aria-pressed={value === device} onClick={() => onChange(device)}>
          {texts[device]}
        </button>
      ))}
    </span>
  );
}

/**
 * The stage a page is shown on (W32): a bar of view choices on top, the page centred as a framed card on a checkered
 * ground, filling the stage's height, and the zoom floating at the bottom right. − and + step, Fit fits the page's
 * width into the stage, Ctrl + scroll zooms the page instead of the browser. Used by Look, Looks and Messages.
 */
export function ZoomBox({ texts, bar, width = null, children }: { texts: { zoom: string; zoomIn: string; zoomOut: string; fit: string }; bar?: ReactNode; width?: number | null; children: ReactNode }) {
  const [zoom, setZoom] = useState(1);
  const [room, setRoom] = useState({ width: 0, height: 0 });
  const canvas = useRef<HTMLDivElement>(null);

  // The room the page has, so the page fills the stage's height and the desktop its width.
  useEffect(() => {
    const element = canvas.current;

    if (element === null || typeof ResizeObserver === "undefined") {
      return undefined;
    }

    const measure = () => setRoom({ width: Math.max(0, element.clientWidth - gutter), height: Math.max(0, element.clientHeight - gutter) });
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    measure();

    return () => observer.disconnect();
  }, []);

  // Ctrl + scroll zooms the page, also over a frame of the same origin, and never the browser's page.
  useEffect(() => {
    const element = canvas.current;

    if (element === null) {
      return undefined;
    }

    const wheel = (event: WheelEvent) => {
      if (!event.ctrlKey && !event.metaKey) {
        return;
      }

      event.preventDefault();
      setZoom((current) => clamp(current * (event.deltaY < 0 ? 1.1 : 1 / 1.1)));
    };
    const windows = new Set<Window>();
    const attach = (frame: HTMLIFrameElement) => {
      try {
        const inner = frame.contentWindow;

        if (inner !== null && !windows.has(inner)) {
          inner.addEventListener("wheel", wheel, { passive: false });
          windows.add(inner);
        }
      } catch {
        // A frame of another origin (the sandboxed email) keeps its own wheel.
      }
    };
    const frames = [...element.querySelectorAll("iframe")];
    const loaded = (event: Event) => attach(event.currentTarget as HTMLIFrameElement);

    element.addEventListener("wheel", wheel, { passive: false });
    frames.forEach((frame) => {
      attach(frame);
      frame.addEventListener("load", loaded);
    });

    return () => {
      element.removeEventListener("wheel", wheel);
      frames.forEach((frame) => frame.removeEventListener("load", loaded));
      windows.forEach((inner) => {
        try {
          inner.removeEventListener("wheel", wheel);
        } catch {
          // Gone with its frame.
        }
      });
    };
  });

  const step = (direction: 1 | -1) =>
    setZoom((current) => (direction === 1 ? (levels.find((level) => level > current + 0.001) ?? largest) : ([...levels].reverse().find((level) => level < current - 0.001) ?? smallest)));
  const fit = () => setZoom(width === null || room.width === 0 ? 1 : clamp(Math.min(1, room.width / width)));

  // The page is laid out at its own size and scaled; its frame takes the scaled size.
  const pageWidth = width ?? (room.width > 0 ? room.width / zoom : null);
  const pageHeight = room.height > 0 ? room.height / zoom : null;
  const frame: CSSProperties = { width: pageWidth === null ? "100%" : `${pageWidth * zoom}px`, height: pageHeight === null ? "100%" : `${room.height}px` };
  const page: CSSProperties = {
    width: pageWidth === null ? `${100 / zoom}%` : `${pageWidth}px`,
    height: pageHeight === null ? `${100 / zoom}%` : `${pageHeight}px`,
    ...(zoom !== 1 && { transform: `scale(${zoom})` }),
  };

  return (
    <div className="aw-stage">
      {bar !== undefined && <div className="aw-stage__bar">{bar}</div>}
      {/* Scrolls when zoomed in, so it takes the keyboard focus too. */}
      <div ref={canvas} className="aw-stage__canvas" role="region" aria-label={texts.zoom} tabIndex={0}>
        <div className="aw-stage__frame" style={frame}>
          <div className="aw-stage__page" style={page}>
            {children}
          </div>
        </div>
      </div>
      <span className="aw-stage__zoom" role="group" aria-label={texts.zoom}>
        <button type="button" aria-label={texts.zoomOut} disabled={zoom <= smallest} onClick={() => step(-1)}>
          −
        </button>
        <output aria-live="polite">{Math.round(zoom * 100)} %</output>
        <button type="button" aria-label={texts.zoomIn} disabled={zoom >= largest} onClick={() => step(1)}>
          +
        </button>
        <button type="button" className="aw-stage__fit" onClick={fit}>
          {texts.fit}
        </button>
      </span>
    </div>
  );
}
