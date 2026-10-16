// SPDX-License-Identifier: AGPL-3.0-only
import { useRef, type KeyboardEvent } from "react";

const length = 6;

type CodeInputProps = {
  /** Id of the visible label; the group of boxes is named by it. */
  labelledBy: string;
  /** The digits so far, at most six. */
  value: string;
  onChange: (value: string) => void;
  /** Names each box for screen readers, e.g. "Digit 3 of 6". */
  digitLabel: (n: number) => string;
  invalid?: boolean;
  autoFocus?: boolean;
  describedBy?: string | undefined;
};

/**
 * The six boxes of a one-time code (AWK-16, M1): a digit moves to the next box, Backspace and the arrows move back,
 * and a pasted or autofilled code (one-time-code on the first box) fills all six.
 */
export function CodeInput({ labelledBy, value, onChange, digitLabel, invalid = false, autoFocus = false, describedBy }: CodeInputProps) {
  const boxes = useRef<(HTMLInputElement | null)[]>([]);
  const digits = value.replace(/\D/g, "").slice(0, length);
  const focus = (index: number) => boxes.current[Math.max(0, Math.min(length - 1, index))]?.focus();

  function typed(index: number, text: string) {
    const entered = text.replace(/\D/g, "");

    if (entered === "") {
      return;
    }

    // One digit fills this box; several (paste, autofill) fill from here on.
    const next = (digits.slice(0, index) + entered).slice(0, length);
    onChange(next);
    focus(next.length);
  }

  function keyDown(index: number, event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Backspace") {
      event.preventDefault();
      const at = index < digits.length ? index : index - 1;

      if (at >= 0) {
        onChange(digits.slice(0, at));
        focus(at);
      }
    } else if (event.key === "ArrowLeft") {
      event.preventDefault();
      focus(index - 1);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      focus(Math.min(index + 1, digits.length));
    }
  }

  return (
    <div role="group" aria-labelledby={labelledBy} aria-describedby={describedBy} className="aw-code">
      {Array.from({ length }, (_, index) => (
        <input
          key={index}
          ref={(element) => {
            boxes.current[index] = element;
          }}
          className="aw-code__box"
          inputMode="numeric"
          autoComplete={index === 0 ? "one-time-code" : "off"}
          aria-label={digitLabel(index + 1)}
          aria-invalid={invalid}
          autoFocus={autoFocus && index === 0}
          value={digits[index] ?? ""}
          onFocus={(event) => event.currentTarget.select()}
          onChange={(event) => typed(index, event.target.value.replace(digits[index] ?? "", "") || event.target.value)}
          onKeyDown={(event) => keyDown(index, event)}
        />
      ))}
    </div>
  );
}
