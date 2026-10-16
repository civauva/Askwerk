// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { createTelemetry, type TelemetryBatch } from "./telemetry";

function recordingTransport() {
  const sent: { url: string; batch: TelemetryBatch }[] = [];
  const transport = (url: string, body: string) => {
    sent.push({ url, batch: JSON.parse(body) as TelemetryBatch });
  };

  return { sent, transport };
}

describe("createTelemetry", () => {
  it("sends queued records as one batch to the App API on flush", () => {
    const { sent, transport } = recordingTransport();
    const telemetry = createTelemetry({ service: "designer", transport, route: () => "/" });

    telemetry.metric("web.lcp", 1200);
    telemetry.error("api.failed", "GET /api/app/status 503", "4bf92f3577b34da6a3ce929d0e0e4736");
    telemetry.flush();

    expect(sent).toEqual([
      {
        url: "/api/app/telemetry",
        batch: {
          service: "designer",
          records: [
            { kind: "metric", name: "web.lcp", value: 1200, route: "/" },
            {
              kind: "log",
              level: "error",
              name: "api.failed",
              message: "GET /api/app/status 503",
              route: "/",
              traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            },
          ],
        },
      },
    ]);
  });

  it("sends nothing when nothing was recorded", () => {
    const { sent, transport } = recordingTransport();
    const telemetry = createTelemetry({ service: "designer", transport, route: () => "/" });

    telemetry.flush();

    expect(sent).toEqual([]);
  });

  it("flushes on its own when 20 records are queued", () => {
    const { sent, transport } = recordingTransport();
    const telemetry = createTelemetry({ service: "runner", transport, route: () => "/" });

    for (let index = 0; index < 20; index++) {
      telemetry.metric("api.duration", index);
    }

    expect(sent).toHaveLength(1);
    expect(sent[0]?.batch.records).toHaveLength(20);
  });

  it("reports the route without query string or fragment", () => {
    const { sent, transport } = recordingTransport();
    const telemetry = createTelemetry({ service: "designer", transport, route: () => "/surveys/42?token=abc#q3" });

    telemetry.metric("web.lcp", 900);
    telemetry.flush();

    expect(sent[0]?.batch.records[0]?.route).toBe("/surveys/42");
  });

  it("leaves out a trace id that is not 32 hex characters", () => {
    const { sent, transport } = recordingTransport();
    const telemetry = createTelemetry({ service: "designer", transport, route: () => "/" });

    telemetry.error("api.failed", "boom", "not-a-trace-id");
    telemetry.flush();

    expect(sent[0]?.batch.records[0]).not.toHaveProperty("traceId");
  });

  it("cuts messages to 500 characters", () => {
    const { sent, transport } = recordingTransport();
    const telemetry = createTelemetry({ service: "designer", transport, route: () => "/" });

    telemetry.error("ui.error", "x".repeat(600));
    telemetry.flush();

    expect(sent[0]?.batch.records[0]).toMatchObject({ message: "x".repeat(500) });
  });
});
