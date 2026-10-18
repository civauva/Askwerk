// SPDX-License-Identifier: AGPL-3.0-only

/** Where a part of the survey is set in Look (W44 A, AWK-69): the area, and the part inside Components. */
export type LookPick = { area: string; part?: string };

/** The survey's parts by their elements, the most specific first; the first that contains the pointer wins. */
export const lookParts: readonly (readonly [selector: string, pick: LookPick])[] = [
  [".run-q__error, .run-message", { area: "components", part: "messages" }],
  [".run-q__title, .run-q__help, .run-q__req", { area: "components", part: "question" }],
  [".run-choices, .run-yesno", { area: "components", part: "choices" }],
  [".run-scale, .run-slider, .run-face", { area: "components", part: "rating" }],
  [".run-matrix", { area: "components", part: "matrix" }],
  [".run-input", { area: "components", part: "fields" }],
  [".run-q--block", { area: "components", part: "text-block" }],
  [".run-progress, .run-ruler, .run-steps", { area: "components", part: "progress" }],
  [".run-nav, .run-button", { area: "components", part: "buttons" }],
  [".run-head, .run-top, .run-org, .run-logo, .run-hero, .run-overtitle", { area: "header" }],
  [".run-welcome", { area: "pages" }],
  [".run-main", { area: "shape" }],
  [".run", { area: "colours" }],
];

export function pickAt(target: Element | null): { element: Element; pick: LookPick } | null {
  for (const [selector, pick] of lookParts) {
    const element = target?.closest(selector);

    if (element !== null && element !== undefined) {
      return { element, pick };
    }
  }

  return null;
}

const key = (pick: LookPick) => (pick.part === undefined ? pick.area : `${pick.area}/${pick.part}`);

/**
 * Turns the preview into a map of its parts: pointing outlines a part and names it, a click sends it to the editor
 * instead of answering. Labels come from the editor, in its language. Returns a function that stops it.
 */
export function startPicking(labels: Readonly<Record<string, string>>, onPick: (pick: LookPick) => void): () => void {
  const style = document.createElement("style");
  style.textContent = `
    [data-look-hover] { outline: 2px dashed #d1440f !important; outline-offset: 4px; cursor: pointer !important; }
    .run-look-label { position: fixed; z-index: 2147483647; padding: 3px 9px; background: #d1440f; color: #fff;
      font: 600 12px/1.4 "IBM Plex Sans", system-ui, sans-serif; border-radius: 5px; pointer-events: none; white-space: nowrap; }
    .run * { cursor: pointer; }`;
  document.head.appendChild(style);
  const label = document.createElement("span");
  label.className = "run-look-label";
  label.setAttribute("aria-hidden", "true");
  let hovered: Element | null = null;

  const clear = () => {
    hovered?.removeAttribute("data-look-hover");
    hovered = null;
    label.remove();
  };

  const over = (event: MouseEvent) => {
    const found = pickAt(event.target as Element);

    if (found?.element === hovered) {
      return;
    }

    clear();

    if (found !== null) {
      hovered = found.element;
      hovered.setAttribute("data-look-hover", "");
      label.textContent = labels[key(found.pick)] ?? labels[found.pick.area] ?? key(found.pick);
      const box = hovered.getBoundingClientRect();
      // On the outline's top-right corner, as in the mockup, so it never covers the text above the part.
      label.style.top = `${Math.max(4, box.top - 16)}px`;
      label.style.left = `${Math.max(4, box.right - 4)}px`;
      label.style.transform = "translateX(-100%)";
      document.body.appendChild(label);
    }
  };

  // Clicks, presses and keys in the survey edit the look instead of answering it.
  const click = (event: MouseEvent) => {
    const found = pickAt(event.target as Element);
    event.preventDefault();
    event.stopPropagation();

    if (found !== null) {
      onPick(found.pick);
    }
  };
  const block = (event: Event) => {
    event.preventDefault();
    event.stopPropagation();
  };

  document.addEventListener("mouseover", over, true);
  document.addEventListener("mouseleave", clear, true);
  document.addEventListener("click", click, true);
  document.addEventListener("mousedown", block, true);
  document.addEventListener("submit", block, true);

  return () => {
    clear();
    style.remove();
    document.removeEventListener("mouseover", over, true);
    document.removeEventListener("mouseleave", clear, true);
    document.removeEventListener("click", click, true);
    document.removeEventListener("mousedown", block, true);
    document.removeEventListener("submit", block, true);
  };
}
