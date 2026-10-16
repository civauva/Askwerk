// SPDX-License-Identifier: AGPL-3.0-only
import { useCallback, useEffect, useRef, useState } from "react";

export type SaveFunction<T> = (value: T) => Promise<void>;

export type SaveStatus =
  | { kind: "saved" }
  | { kind: "pending" }
  | { kind: "failed"; retryInSeconds: number; traceId: string | undefined };

export interface AutosaveOptions {
  /** Pause after the last edit before saving. */
  delayMs: number;
  /** Wait after a failed save before trying again on its own. */
  retryMs: number;
  /** Reads the reference (trace id) from a save error, shown to the user. */
  traceIdOf: (error: unknown) => string | undefined;
}

/**
 * Saves the value once edits pause. A failed save keeps everything on screen, shows its reference and a
 * countdown, and tries again on its own (M2 "Save failed" in AWK-5); retryNow tries at once.
 */
export function useAutosave<T>(value: T, save: SaveFunction<T>, options: AutosaveOptions): { status: SaveStatus; retryNow: () => void } {
  const [status, setStatus] = useState<SaveStatus>({ kind: "saved" });
  const latest = useRef(value);
  const lastSaved = useRef(value);
  const saveRef = useRef(save);
  const optionsRef = useRef(options);
  const retryTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const countdown = useRef<ReturnType<typeof setInterval> | undefined>(undefined);

  latest.current = value;
  saveRef.current = save;
  optionsRef.current = options;

  const clearRetry = useCallback(() => {
    clearTimeout(retryTimer.current);
    clearInterval(countdown.current);
  }, []);

  const attempt = useCallback(() => {
    clearRetry();
    const saving = latest.current;

    saveRef.current(saving).then(
      () => {
        lastSaved.current = saving;

        if (latest.current === saving) {
          setStatus({ kind: "saved" });
        }
      },
      (error: unknown) => {
        const { retryMs, traceIdOf } = optionsRef.current;
        const traceId = traceIdOf(error);
        let retryInSeconds = Math.round(retryMs / 1000);

        setStatus({ kind: "failed", retryInSeconds, traceId });
        countdown.current = setInterval(() => {
          retryInSeconds = Math.max(0, retryInSeconds - 1);
          setStatus({ kind: "failed", retryInSeconds, traceId });
        }, 1000);
        retryTimer.current = setTimeout(attempt, retryMs);
      },
    );
  }, [clearRetry]);

  useEffect(() => {
    if (value === lastSaved.current) {
      return undefined;
    }

    clearRetry();
    setStatus({ kind: "pending" });
    const timer = setTimeout(attempt, optionsRef.current.delayMs);

    return () => clearTimeout(timer);
  }, [value, attempt, clearRetry]);

  useEffect(() => clearRetry, [clearRetry]);

  return { status, retryNow: attempt };
}
