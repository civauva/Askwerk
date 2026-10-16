// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { installGlobalErrorHandlers, observeWebVitals, type PerformanceObserverLike } from "./browser";
import type { Telemetry } from "./telemetry";

function recordingTelemetry() {
  const records: { kind: string; name: string; value?: number; message?: string }[] = [];
  const telemetry: Telemetry = {
    metric: (name, value) => records.push({ kind: "metric", name, value }),
    error: (name, message) => records.push({ kind: "error", name, message }),
    warn: (name, message) => records.push({ kind: "warn", name, message }),
    flush: () => records.push({ kind: "flush", name: "" }),
  };

  return { records, telemetry };
}

describe("installGlobalErrorHandlers", () => {
  it("records uncaught errors", () => {
    const { records, telemetry } = recordingTelemetry();
    const target = new EventTarget();
    installGlobalErrorHandlers(target, telemetry);

    target.dispatchEvent(Object.assign(new Event("error"), { message: "x is not a function" }));

    expect(records).toContainEqual({ kind: "error", name: "ui.error", message: "x is not a function" });
  });

  it("records unhandled promise rejections", () => {
    const { records, telemetry } = recordingTelemetry();
    const target = new EventTarget();
    installGlobalErrorHandlers(target, telemetry);

    target.dispatchEvent(Object.assign(new Event("unhandledrejection"), { reason: new Error("timeout") }));

    expect(records).toContainEqual({ kind: "error", name: "ui.unhandled-rejection", message: "timeout" });
  });

  it("flushes when the page is hidden, so nothing is lost on navigation", () => {
    const { records, telemetry } = recordingTelemetry();
    const target = new EventTarget();
    installGlobalErrorHandlers(target, telemetry);

    target.dispatchEvent(new Event("pagehide"));

    expect(records).toContainEqual({ kind: "flush", name: "" });
  });
});

describe("observeWebVitals", () => {
  function fakeObserver() {
    const callbacks = new Map<string, (entries: PerformanceEntry[]) => void>();
    const Observer: PerformanceObserverLike = class {
      constructor(private readonly callback: (list: { getEntries(): PerformanceEntry[] }) => void) {}

      observe(options: { type: string; buffered?: boolean }) {
        callbacks.set(options.type, (entries) => this.callback({ getEntries: () => entries }));
      }
    };

    return { Observer, emit: (type: string, entries: object[]) => callbacks.get(type)?.(entries as PerformanceEntry[]) };
  }

  it("reports the largest contentful paint", () => {
    const { records, telemetry } = recordingTelemetry();
    const { Observer, emit } = fakeObserver();
    observeWebVitals(Observer, telemetry);

    emit("largest-contentful-paint", [{ startTime: 800 }, { startTime: 1250 }]);

    expect(records).toContainEqual({ kind: "metric", name: "web.lcp", value: 1250 });
  });

  it("reports time to first byte from the navigation entry", () => {
    const { records, telemetry } = recordingTelemetry();
    const { Observer, emit } = fakeObserver();
    observeWebVitals(Observer, telemetry);

    emit("navigation", [{ responseStart: 180 }]);

    expect(records).toContainEqual({ kind: "metric", name: "web.ttfb", value: 180 });
  });

  it("adds up layout shifts that were not caused by user input", () => {
    const { records, telemetry } = recordingTelemetry();
    const { Observer, emit } = fakeObserver();
    observeWebVitals(Observer, telemetry);

    emit("layout-shift", [
      { value: 0.05, hadRecentInput: false },
      { value: 0.5, hadRecentInput: true },
      { value: 0.02, hadRecentInput: false },
    ]);

    expect(records).toContainEqual({ kind: "metric", name: "web.cls", value: 0.07 });
  });

  it("reports the slowest interaction as INP", () => {
    const { records, telemetry } = recordingTelemetry();
    const { Observer, emit } = fakeObserver();
    observeWebVitals(Observer, telemetry);

    emit("event", [
      { duration: 40, interactionId: 1 },
      { duration: 120, interactionId: 2 },
      { duration: 300, interactionId: 0 },
    ]);

    expect(records).toContainEqual({ kind: "metric", name: "web.inp", value: 120 });
  });
});
