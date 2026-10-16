// SPDX-License-Identifier: AGPL-3.0-only

/** One signal sent to POST /api/app/telemetry (agents/observability.md). */
export type TelemetryRecord =
  | { kind: "metric"; name: string; value: number; route: string }
  | { kind: "log"; level: "error" | "warn"; name: string; message: string; route: string; traceId?: string };

/** The request body of POST /api/app/telemetry. */
export type TelemetryBatch = { service: "designer" | "runner"; records: TelemetryRecord[] };

/** Sends a serialised batch; the browser default uses navigator.sendBeacon. */
export type Transport = (url: string, body: string) => void;

/** What the apps call. Records are queued and sent in batches. */
export interface Telemetry {
  metric(name: string, value: number): void;
  error(name: string, message: string, traceId?: string): void;
  warn(name: string, message: string, traceId?: string): void;
  flush(): void;
}

export type TelemetryOptions = {
  service: "designer" | "runner";
  transport: Transport;
  /** The current route; query string and fragment are removed before sending. */
  route: () => string;
  endpoint?: string;
};

const autoFlushAt = 20;
const maxMessageLength = 500;
const traceIdPattern = /^[0-9a-f]{32}$/;

export function createTelemetry(options: TelemetryOptions): Telemetry {
  const endpoint = options.endpoint ?? "/api/app/telemetry";
  let queue: TelemetryRecord[] = [];

  const currentRoute = () => options.route().split(/[?#]/, 1)[0] ?? "";

  const flush = () => {
    if (queue.length === 0) {
      return;
    }

    const batch: TelemetryBatch = { service: options.service, records: queue };
    queue = [];
    options.transport(endpoint, JSON.stringify(batch));
  };

  const enqueue = (record: TelemetryRecord) => {
    queue.push(record);

    if (queue.length >= autoFlushAt) {
      flush();
    }
  };

  const log = (level: "error" | "warn", name: string, message: string, traceId?: string) => {
    const record: TelemetryRecord = {
      kind: "log",
      level,
      name,
      message: message.slice(0, maxMessageLength),
      route: currentRoute(),
    };

    if (traceId !== undefined && traceIdPattern.test(traceId)) {
      record.traceId = traceId;
    }

    enqueue(record);
  };

  return {
    metric: (name, value) => enqueue({ kind: "metric", name, value, route: currentRoute() }),
    error: (name, message, traceId) => log("error", name, message, traceId),
    warn: (name, message, traceId) => log("warn", name, message, traceId),
    flush,
  };
}
