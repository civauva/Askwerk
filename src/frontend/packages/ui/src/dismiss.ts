// SPDX-License-Identifier: AGPL-3.0-only
import { useEffect, useRef, type RefObject } from "react";

/**
 * Closes an open popup (list, calendar, menu) the way people expect (AWK-19): a click outside, Escape wherever the
 * focus is, or the focus moving on to something outside. Escape gives the focus back to the control that opened it;
 * a click or Tab leaves the focus where the person put it.
 */
export function useDismiss(open: boolean, container: RefObject<HTMLElement | null>, close: (refocus: boolean) => void): void {
  const latest = useRef(close);
  latest.current = close;

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const outside = (target: EventTarget | null) => target instanceof Node && container.current !== null && !container.current.contains(target);
    const onPointerDown = (event: Event) => outside(event.target) && latest.current(false);
    const onFocusIn = (event: FocusEvent) => outside(event.target) && latest.current(false);
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        latest.current(true);
      }
    };

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("touchstart", onPointerDown);
    document.addEventListener("focusin", onFocusIn);
    document.addEventListener("keydown", onKeyDown);

    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("touchstart", onPointerDown);
      document.removeEventListener("focusin", onFocusIn);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, container]);
}
