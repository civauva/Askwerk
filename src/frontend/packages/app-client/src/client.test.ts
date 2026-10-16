// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { ApiError, createAppClient } from "./client";

function recordingTelemetry() {
  const records: unknown[] = [];
  const telemetry: Telemetry = {
    metric: (name, value) => records.push({ kind: "metric", name, value }),
    error: (name: string, message: string, traceId?: string) => records.push({ kind: "error", name, message, traceId }),
    warn: (name: string, message: string, traceId?: string) => records.push({ kind: "warn", name, message, traceId }),
    flush: () => undefined,
  };

  return { records, telemetry };
}

function fakeFetch(respond: () => Response | Promise<Response>) {
  const requests: Request[] = [];
  const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
    return respond();
  };

  return { requests, fetch };
}

const status = { version: "0.1.0", commit: "3c4a89a", startedAt: "2026-10-13T08:00:00Z", database: "ready" };
const clock = (...times: number[]) => () => times.shift() ?? 0;

describe("createAppClient", () => {
  it("returns the status from GET /api/app/status", async () => {
    const { fetch } = fakeFetch(() => Response.json(status));
    const client = createAppClient({ fetch, telemetry: recordingTelemetry().telemetry });

    const result = await client.getStatus();

    expect(result).toEqual(status);
  });

  it("sends a W3C traceparent header with every call", async () => {
    const { requests, fetch } = fakeFetch(() => Response.json(status));
    const client = createAppClient({ fetch, telemetry: recordingTelemetry().telemetry });

    await client.getStatus();

    expect(requests[0]?.headers.get("traceparent")).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
  });

  it("records how long the call took", async () => {
    const { fetch } = fakeFetch(() => Response.json(status));
    const { records, telemetry } = recordingTelemetry();
    const client = createAppClient({ fetch, telemetry, now: clock(1000, 1042) });

    await client.getStatus();

    expect(records).toContainEqual({ kind: "metric", name: "api.duration", value: 42 });
  });

  it("throws an ApiError with the server's trace id when the API answers with a problem", async () => {
    const problem = { status: 503, title: "Service Unavailable", traceId: "a3ce929d0e0e4736a3ce929d0e0e4736" };
    const { fetch } = fakeFetch(() => Response.json(problem, { status: 503, headers: { "Content-Type": "application/problem+json" } }));
    const { records, telemetry } = recordingTelemetry();
    const client = createAppClient({ fetch, telemetry });

    const error = await client.getStatus().catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 503, traceId: "a3ce929d0e0e4736a3ce929d0e0e4736" });
    expect(records).toContainEqual({
      kind: "error",
      name: "api.failed",
      message: "GET /api/app/status 503",
      traceId: "a3ce929d0e0e4736a3ce929d0e0e4736",
    });
  });

  it("throws an ApiError with status 0 and its own trace id when the network fails", async () => {
    const { requests, fetch } = fakeFetch(() => Promise.reject(new TypeError("Failed to fetch")));
    const { records, telemetry } = recordingTelemetry();
    const client = createAppClient({ fetch, telemetry });

    const error = await client.getStatus().catch((caught: unknown) => caught);

    const sentTraceId = requests[0]?.headers.get("traceparent")?.slice(3, 35);
    expect(error).toMatchObject({ status: 0, traceId: sentTraceId });
    expect(records).toContainEqual({ kind: "error", name: "api.failed", message: "GET /api/app/status network error", traceId: sentTraceId });
  });
});
