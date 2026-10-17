// SPDX-License-Identifier: AGPL-3.0-only
// Test helpers for moving pages and questions by pointer (W34). jsdom lays nothing out, so a drop place is given a box.
import { fireEvent } from "@testing-library/react";

/** Gives the drop place that holds the element (or the element itself) a box from top to top + height. */
export function placeAt(element: Element, top: number, height = 40) {
  const place = element.closest("[data-drop-kind]") ?? element;
  place.getBoundingClientRect = () => ({ top, height, bottom: top + height, left: 0, right: 200, width: 200, x: 0, y: top, toJSON: () => ({}) });
}

/** Presses the pointer on a handle; the drag starts with the first move. */
export function grab(handle: Element) {
  fireEvent.pointerDown(handle, { button: 0, clientX: 10, clientY: 0, pointerId: 1, pointerType: "mouse" });
}

export function moveTo(y: number) {
  fireEvent.pointerMove(window, { clientX: 10, clientY: y, pointerId: 1, pointerType: "mouse" });
}

export function release() {
  fireEvent.pointerUp(window, { clientX: 10, pointerId: 1, pointerType: "mouse" });
}

/** The accent line that marks where the dragged item lands, if one shows. */
export const dropLine = () => document.querySelector(".designer-drop");
/** The lifted copy that follows the pointer, if one shows. */
export const lift = () => document.querySelector(".designer-lift");
