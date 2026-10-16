// SPDX-License-Identifier: AGPL-3.0-only
import "./logo.css";

/** The Askwerk logo: the angular "?" tile and the wordmark (agents/ui-design-system.md, Logo). */
export function Logo() {
  return (
    <span className="designer-logo">
      <svg width="20" height="20" viewBox="0 0 32 32" aria-hidden="true">
        <rect width="32" height="32" rx="3" className="designer-logo__tile" />
        <path d="M10 11V8h12v8h-6v4" fill="none" className="designer-logo__mark" strokeWidth="3.6" strokeLinecap="square" />
        <rect x="14.2" y="22.6" width="3.6" height="3.6" className="designer-logo__dot" />
      </svg>
      <span className="designer-logo__word">
        ask<b>werk</b>
      </span>
    </span>
  );
}
