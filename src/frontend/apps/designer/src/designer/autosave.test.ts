// SPDX-License-Identifier: AGPL-3.0-only
import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useAutosave, type SaveFunction } from "./autosave";

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

class SaveFailed extends Error {
  constructor(readonly traceId: string) {
    super("save failed");
  }
}

function saver(...outcomes: ("ok" | "fail")[]) {
  const save = vi.fn<SaveFunction<string>>(() => {
    const outcome = outcomes.length > 1 ? outcomes.shift()! : outcomes[0]!;

    return outcome === "ok" ? Promise.resolve() : Promise.reject(new SaveFailed("4bf92f3577b34da6a3ce929d0e0e4736"));
  });

  return save;
}

const options = { delayMs: 800, retryMs: 8000, traceIdOf: (error: unknown) => (error instanceof SaveFailed ? error.traceId : undefined) };

describe("useAutosave", () => {
  it("is saved and does not save the value it started with", async () => {
    const save = saver("ok");

    const { result } = renderHook(() => useAutosave("v1", save, options));
    await act(() => vi.advanceTimersByTimeAsync(5000));

    expect(result.current.status).toEqual({ kind: "saved" });
    expect(save).not.toHaveBeenCalled();
  });

  it("saves the latest value once the edits pause", async () => {
    const save = saver("ok");
    const { rerender } = renderHook(({ value }) => useAutosave(value, save, options), { initialProps: { value: "v1" } });

    rerender({ value: "v2" });
    await act(() => vi.advanceTimersByTimeAsync(400));
    rerender({ value: "v3" });
    await act(() => vi.advanceTimersByTimeAsync(800));

    expect(save).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledWith("v3");
  });

  it("shows unsaved changes while waiting and saving, and saved afterwards", async () => {
    const save = saver("ok");
    const { result, rerender } = renderHook(({ value }) => useAutosave(value, save, options), { initialProps: { value: "v1" } });

    rerender({ value: "v2" });
    const whileWaiting = result.current.status;
    await act(() => vi.advanceTimersByTimeAsync(800));

    expect(whileWaiting).toEqual({ kind: "pending" });
    expect(result.current.status).toEqual({ kind: "saved" });
  });

  it("reports a failed save with its reference and the seconds until the next try", async () => {
    const save = saver("fail");
    const { result, rerender } = renderHook(({ value }) => useAutosave(value, save, options), { initialProps: { value: "v1" } });

    rerender({ value: "v2" });
    await act(() => vi.advanceTimersByTimeAsync(800));

    expect(result.current.status).toEqual({ kind: "failed", retryInSeconds: 8, traceId: "4bf92f3577b34da6a3ce929d0e0e4736" });
  });

  it("counts down to the next try", async () => {
    const save = saver("fail");
    const { result, rerender } = renderHook(({ value }) => useAutosave(value, save, options), { initialProps: { value: "v1" } });
    rerender({ value: "v2" });
    await act(() => vi.advanceTimersByTimeAsync(800));

    await act(() => vi.advanceTimersByTimeAsync(3000));

    expect(result.current.status).toEqual({ kind: "failed", retryInSeconds: 5, traceId: "4bf92f3577b34da6a3ce929d0e0e4736" });
  });

  it("tries again on its own after the retry delay and is saved when that works", async () => {
    const save = saver("fail", "ok");
    const { result, rerender } = renderHook(({ value }) => useAutosave(value, save, options), { initialProps: { value: "v1" } });
    rerender({ value: "v2" });
    await act(() => vi.advanceTimersByTimeAsync(800));

    await act(() => vi.advanceTimersByTimeAsync(8000));

    expect(save).toHaveBeenCalledTimes(2);
    expect(result.current.status).toEqual({ kind: "saved" });
  });

  it("tries again at once when asked", async () => {
    const save = saver("fail", "ok");
    const { result, rerender } = renderHook(({ value }) => useAutosave(value, save, options), { initialProps: { value: "v1" } });
    rerender({ value: "v2" });
    await act(() => vi.advanceTimersByTimeAsync(800));

    await act(async () => result.current.retryNow());

    expect(save).toHaveBeenCalledTimes(2);
    expect(result.current.status).toEqual({ kind: "saved" });
  });
});
