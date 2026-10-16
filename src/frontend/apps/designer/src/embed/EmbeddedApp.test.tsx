// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type EmbedSession } from "@askwerk/app-client";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { EmbeddedApp, type HostEvent } from "./EmbeddedApp";

afterEach(cleanup);

const telemetry = { error: vi.fn(), event: vi.fn(), metric: vi.fn() } as never;
const session: EmbedSession = { token: "aws_x", expiresAt: "2026-10-15T13:00:00Z", endsAt: "2026-10-15T20:00:00Z", surveyId: "s1", view: "results", language: "de" };

describe("Embedded designer and Results (AWK-13, W21)", () => {
  it("starts a session from the host's token and tells the host it is ready", async () => {
    const post = vi.fn<(event: HostEvent) => void>();
    const start = vi.fn(() => Promise.resolve(session));
    render(<EmbeddedApp token="eyJ.a.b" view="results" lang={null} telemetry={telemetry} post={post} start={start} renew={() => Promise.resolve(session)} now={() => Date.parse("2026-10-15T12:00:00Z")} />);

    await waitFor(() => expect(post).toHaveBeenCalledWith({ type: "askwerk:ready", surveyId: "s1", view: "results" }));
    expect(start).toHaveBeenCalledWith({ telemetry }, "eyJ.a.b", "results");
  });

  it.each([
    ["token-invalid", "en", "The sign-in link has expired. Reload the page."],
    ["embedding-off", "de", "Die Einbettung ist für diese Organisation ausgeschaltet."],
  ] as const)("shows %s in the frame and tells the host", async (code, lang, text) => {
    const post = vi.fn<(event: HostEvent) => void>();
    render(<EmbeddedApp token="t" view="designer" lang={lang} telemetry={telemetry} post={post} start={() => Promise.reject(new ApiError(401, undefined, "no", { code }))} />);

    expect((await screen.findByRole("alert")).textContent).toBe(text);
    expect(post).toHaveBeenCalledWith({ type: "askwerk:error", code });
  });
});
