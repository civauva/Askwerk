// SPDX-License-Identifier: AGPL-3.0-only
import type { ReactNode } from "react";

type ButtonProps = {
  onClick?: () => void;
  variant?: "primary" | "quiet";
  pressed?: boolean;
  children: ReactNode;
};

/** The one button. Quiet buttons are for secondary actions such as the language switch. */
export function Button({ onClick, variant = "primary", pressed, children }: ButtonProps) {
  return (
    <button type="button" className={`aw-button aw-button--${variant}`} onClick={onClick} aria-pressed={pressed}>
      {children}
    </button>
  );
}
