// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { newTraceparent } from "./trace";

describe("newTraceparent", () => {
  it("creates a W3C traceparent header and returns its trace id", () => {
    const { traceparent, traceId } = newTraceparent();

    expect(traceparent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
    expect(traceparent.slice(3, 35)).toBe(traceId);
  });

  it("creates a different trace id every time", () => {
    const first = newTraceparent();
    const second = newTraceparent();

    expect(first.traceId).not.toBe(second.traceId);
  });
});
