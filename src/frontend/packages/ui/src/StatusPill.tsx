// SPDX-License-Identifier: AGPL-3.0-only
import type { ReactNode } from "react";

/** A state shown as a dot plus text, so it never relies on colour alone (WCAG 1.4.1). */
export function StatusPill({ tone, children }: { tone: "positive" | "negative"; children: ReactNode }) {
  return (
    <span className={`aw-pill aw-pill--${tone}`}>
      <span aria-hidden="true" className="aw-pill__dot" />
      {children}
    </span>
  );
}
