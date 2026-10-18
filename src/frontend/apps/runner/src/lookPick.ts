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
  // The page behind the survey: its background, chosen on Style (AWK-74: a click opens the picture panel).
  [".run", { area: "background" }],
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
 * Turns the preview into a map of its parts (Edit, W46 A): pointing outlines a part and names it; a click on it or
 * its label sends it to the editor instead of answering, Next included. Try in the editor turns this off, and the
 * preview is the survey. Labels come from the editor, in its language. Returns a function that stops it.
 */
export function startPicking(labels: Readonly<Record<string, string>>, onPick: (pick: LookPick) => void): () => void {
  const style = document.createElement("style");
  style.textContent = `
    [data-look-hover] { outline: 2px dashed #d1440f !important; outline-offset: 4px; cursor: pointer !important; }
    .run-look-label { position: fixed; z-index: 2147483647; padding: 3px 9px; background: #d1440f; color: #fff;
      font: 600 12px/1.4 "IBM Plex Sans", system-ui, sans-serif; border-radius: 5px; white-space: nowrap; cursor: pointer; }
    .run-look-label:hover { background: #b23a0d; }
    .run * { cursor: pointer; }`;
  document.head.appendChild(style);
  const label = document.createElement("span");
  label.className = "run-look-label";
  label.setAttribute("aria-hidden", "true");
  let hovered: { element: Element; pick: LookPick } | null = null;

  const clear = () => {
    hovered?.element.removeAttribute("data-look-hover");
    hovered = null;
    label.remove();
  };

  const over = (event: MouseEvent) => {
    // On its way to the label the pointer leaves the part; the label stays.
    if (event.target === label) {
      return;
    }

    const found = pickAt(event.target as Element);

    if (found?.element === hovered?.element) {
      return;
    }

    clear();

    if (found !== null) {
      hovered = found;
      found.element.setAttribute("data-look-hover", "");
      label.textContent = labels[key(found.pick)] ?? labels[found.pick.area] ?? key(found.pick);
      const box = found.element.getBoundingClientRect();
      // On the outline's top-right corner, as in the mockup, half over the part: the pointer reaches it without leaving
      // the part, and it never covers the text above.
      label.style.top = `${Math.max(4, box.top - 10)}px`;
      label.style.left = `${Math.max(4, box.right - 4)}px`;
      label.style.transform = "translateX(-100%)";
      document.body.appendChild(label);
    }
  };

  // A click on a part, or on its label, opens its settings; answers are not given here.
  const click = (event: MouseEvent) => {
    const target = event.target as Element;

    if (target === label) {
      event.preventDefault();
      event.stopPropagation();

      if (hovered !== null) {
        onPick(hovered.pick);
      }

      return;
    }

    const found = pickAt(target);
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
  // Only leaving the whole preview clears it; parts are left and entered through mouseover.
  document.documentElement.addEventListener("mouseleave", clear);
  document.addEventListener("click", click, true);
  document.addEventListener("mousedown", block, true);
  document.addEventListener("submit", block, true);

  return () => {
    clear();
    style.remove();
    document.removeEventListener("mouseover", over, true);
    document.documentElement.removeEventListener("mouseleave", clear);
    document.removeEventListener("click", click, true);
    document.removeEventListener("mousedown", block, true);
    document.removeEventListener("submit", block, true);
  };
}
