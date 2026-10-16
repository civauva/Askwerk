// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry, Transport } from "./telemetry";

/** The parts of PerformanceObserver we use, so tests can pass a fake. */
export type PerformanceObserverLike = new (callback: (list: { getEntries(): PerformanceEntry[] }) => void) => {
  observe(options: { type: string; buffered?: boolean }): void;
};

type LayoutShift = PerformanceEntry & { value: number; hadRecentInput: boolean };
type EventTiming = PerformanceEntry & { interactionId?: number };
type NavigationTiming = PerformanceEntry & { responseStart: number };

/** Records uncaught errors and unhandled rejections, and sends what is queued when the page goes away. */
export function installGlobalErrorHandlers(target: EventTarget, telemetry: Telemetry): void {
  target.addEventListener("error", (event) => {
    telemetry.error("ui.error", (event as ErrorEvent).message ?? "Unknown error");
  });

  target.addEventListener("unhandledrejection", (event) => {
    const reason = (event as PromiseRejectionEvent).reason as unknown;
    telemetry.error("ui.unhandled-rejection", reason instanceof Error ? reason.message : String(reason));
  });

  target.addEventListener("pagehide", () => telemetry.flush());
}

/** Measures Core Web Vitals with the browser's own PerformanceObserver (no library). */
export function observeWebVitals(Observer: PerformanceObserverLike, telemetry: Telemetry): void {
  observe(Observer, "largest-contentful-paint", (entries) => {
    const last = entries.at(-1);

    if (last) {
      telemetry.metric("web.lcp", last.startTime);
    }
  });

  observe(Observer, "navigation", (entries) => {
    const navigation = entries[0] as NavigationTiming | undefined;

    if (navigation) {
      telemetry.metric("web.ttfb", navigation.responseStart);
    }
  });

  let cumulativeShift = 0;
  observe(Observer, "layout-shift", (entries) => {
    for (const shift of entries as LayoutShift[]) {
      if (!shift.hadRecentInput) {
        cumulativeShift += shift.value;
      }
    }

    telemetry.metric("web.cls", Math.round(cumulativeShift * 10_000) / 10_000);
  });

  let slowestInteraction = 0;
  observe(Observer, "event", (entries) => {
    for (const timing of entries as EventTiming[]) {
      if ((timing.interactionId ?? 0) > 0 && timing.duration > slowestInteraction) {
        slowestInteraction = timing.duration;
        telemetry.metric("web.inp", slowestInteraction);
      }
    }
  });
}

/** Sends with navigator.sendBeacon, which survives page unloads; falls back to fetch with keepalive. */
export function beaconTransport(): Transport {
  return (url, body) => {
    const blob = new Blob([body], { type: "application/json" });

    if (typeof navigator.sendBeacon === "function" && navigator.sendBeacon(url, blob)) {
      return;
    }

    void fetch(url, { method: "POST", body, keepalive: true, headers: { "Content-Type": "application/json" } });
  };
}

function observe(Observer: PerformanceObserverLike, type: string, handle: (entries: PerformanceEntry[]) => void) {
  try {
    new Observer((list) => handle(list.getEntries())).observe({ type, buffered: true });
  } catch {
    // Older browsers do not know every entry type; that signal is simply not reported.
  }
}
