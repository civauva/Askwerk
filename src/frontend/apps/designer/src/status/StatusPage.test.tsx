// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type AppClient, type StatusResponse } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StatusPage } from "./StatusPage";

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

const startedAt = "2026-10-13T08:00:00Z";
const twoMinutesLater = () => new Date("2026-10-13T08:02:00Z");
const ready: StatusResponse = { version: "0.0.0-dev", commit: "3c4a89a", startedAt, database: "ready" };
const notReady: StatusResponse = { ...ready, database: "notReady" };

function clientReturning(...answers: (StatusResponse | Error | "pending")[]) {
  const getStatus = vi.fn(() => {
    const answer = answers.length > 1 ? answers.shift()! : answers[0]!;

    if (answer === "pending") {
      return new Promise<StatusResponse>(() => undefined);
    }

    return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer);
  });
  const client: AppClient = { getStatus };

  return { client, getStatus };
}

describe("StatusPage", () => {
  it("shows a skeleton while loading, announced as busy", () => {
    const { client } = clientReturning("pending");

    render(<StatusPage client={client} initialLocale="en" now={twoMinutesLater} />);

    expect(screen.getByRole("status", { name: "Loading" }).getAttribute("aria-busy")).toBe("true");
  });

  it.each([
    ["de", "Askwerk läuft", "Datenbank bereit", "Gestartet vor 2 Min."],
    ["en", "Askwerk is running", "Database ready", "Started 2 min ago"],
  ] as const)("shows the running state in %s", async (locale, heading, pill, started) => {
    const { client } = clientReturning(ready);

    render(<StatusPage client={client} initialLocale={locale} now={twoMinutesLater} />);

    expect(await screen.findByRole("heading", { name: heading })).toBeTruthy();
    expect(screen.getByText(pill)).toBeTruthy();
    expect(screen.getByText(started)).toBeTruthy();
    expect(screen.getByText("Version 0.0.0-dev · 3c4a89a")).toBeTruthy();
  });

  it.each([
    ["de", "Askwerk startet", "Datenbank nicht bereit"],
    ["en", "Askwerk is starting", "Database not ready"],
  ] as const)("shows the starting state in %s while the database is not ready", async (locale, heading, pill) => {
    const { client } = clientReturning(notReady);

    render(<StatusPage client={client} initialLocale={locale} now={twoMinutesLater} />);

    expect(await screen.findByRole("heading", { name: heading })).toBeTruthy();
    expect(screen.getByText(pill)).toBeTruthy();
  });

  it("asks again every 5 seconds while the database is not ready, until it is", async () => {
    vi.useFakeTimers();
    const { client, getStatus } = clientReturning(notReady, ready);
    render(<StatusPage client={client} initialLocale="en" now={twoMinutesLater} />);
    await act(() => vi.advanceTimersByTimeAsync(0));

    await act(() => vi.advanceTimersByTimeAsync(5000));

    expect(getStatus).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("heading", { name: "Askwerk is running" })).toBeTruthy();
  });

  it.each([
    ["de", "Etwas ist schiefgelaufen", "Erneut versuchen", "Referenz: a3ce929d0e0e4736a3ce929d0e0e4736"],
    ["en", "Something went wrong", "Try again", "Reference: a3ce929d0e0e4736a3ce929d0e0e4736"],
  ] as const)("shows the error state in %s with the trace id as reference", async (locale, heading, button, reference) => {
    const { client } = clientReturning(new ApiError(0, "a3ce929d0e0e4736a3ce929d0e0e4736", "network"));

    render(<StatusPage client={client} initialLocale={locale} now={twoMinutesLater} />);

    expect(await screen.findByRole("heading", { name: heading })).toBeTruthy();
    expect(screen.getByRole("button", { name: button })).toBeTruthy();
    expect(screen.getByText(reference)).toBeTruthy();
  });

  it("tries again when the button is pressed", async () => {
    const { client, getStatus } = clientReturning(new ApiError(0, undefined, "network"), ready);
    render(<StatusPage client={client} initialLocale="en" now={twoMinutesLater} />);

    fireEvent.click(await screen.findByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("heading", { name: "Askwerk is running" })).toBeTruthy();
    expect(getStatus).toHaveBeenCalledTimes(2);
  });

  it("switches the language", async () => {
    const { client } = clientReturning(ready);
    render(<StatusPage client={client} initialLocale="en" now={twoMinutesLater} />);
    await screen.findByRole("heading", { name: "Askwerk is running" });

    fireEvent.click(screen.getByRole("button", { name: "Deutsch" }));

    expect(screen.getByRole("heading", { name: "Askwerk läuft" })).toBeTruthy();
  });

  it.each([["loading"], ["running"], ["starting"], ["error"]] as const)("has no accessibility violations when %s", async (state) => {
    const answer = { loading: "pending", running: ready, starting: notReady, error: new ApiError(503, undefined, "x") }[state] as
      | StatusResponse
      | Error
      | "pending";
    const { client } = clientReturning(answer);

    const { container } = render(<StatusPage client={client} initialLocale="de" now={twoMinutesLater} />);
    await act(() => Promise.resolve());

    await expectNoAxeViolations(container);
  });

  it("leaves out its own bar when it sits under the app bar", async () => {
    const { client } = clientReturning(ready);

    render(<StatusPage client={client} initialLocale="en" now={twoMinutesLater} embedded />);

    expect(await screen.findByRole("heading", { name: "Askwerk is running" })).toBeTruthy();
    expect(screen.queryByRole("banner")).toBeNull();
  });
});
