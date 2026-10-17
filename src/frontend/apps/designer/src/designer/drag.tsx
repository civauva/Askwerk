// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyDefinition } from "@askwerk/schema";
import { useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent, type RefObject } from "react";
import type { DesignerAction } from "./state";
import type { RegionProps } from "./text";

/**
 * Moving pages and questions by pointer (W34), shared by the page list and the canvas: the item lifts and follows the
 * pointer, its place stays as a faint outline, and an accent line with a dot shows where it lands. Escape cancels.
 * Pointer events, not HTML drag and drop, so the lifted copy can follow the pointer and tilt.
 *
 * Drop places are marked in the DOM: `data-drop-kind="question" | "page-start" | "page"` and `data-drop-id`.
 * A question lands before or after a question, or at the start of a page (also an empty one); a page lands before
 * or after a page.
 */
export type DragKind = "question" | "page";
export type DropTarget = { kind: "question" | "page"; id: string; after: boolean } | { kind: "page-start"; id: string };
export type Drag = { kind: DragKind; id: string; label: string; x: number; y: number; dx: number; dy: number; target: DropTarget | null };
/** Where the marker goes on an item: before it, after it, or (on a page header) at the start of the page. */
export type DropMark = "before" | "after" | "start";

const threshold = 4;
const edge = 40;

export function useDrag(container: RefObject<HTMLElement | null>, onDrop: (kind: DragKind, id: string, target: DropTarget) => void, onCancel?: () => void) {
  const [drag, setDrag] = useState<Drag | null>(null);
  const stop = useRef<(() => void) | null>(null);
  const dropRef = useRef(onDrop);
  dropRef.current = onDrop;
  const cancelRef = useRef(onCancel);
  cancelRef.current = onCancel;

  useEffect(() => () => stop.current?.(), []);

  function start(event: ReactPointerEvent<HTMLElement>, kind: DragKind, id: string, label: string) {
    const grip = (event.target as HTMLElement).closest("[data-grip]") !== null;

    if (event.button !== 0 || (event.pointerType === "touch" && !grip)) {
      return;
    }

    stop.current?.();
    const box = event.currentTarget.getBoundingClientRect();
    const from = { x: event.clientX, y: event.clientY };
    const dx = Math.min(Math.max(event.clientX - box.left, 0), 24);
    const dy = Math.min(Math.max(event.clientY - box.top, 0), 18);
    let current: Drag | null = null;

    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      // Capture is a nicety; the window listeners below still follow the pointer.
    }

    const move = (moved: PointerEvent) => {
      if (current === null && Math.hypot(moved.clientX - from.x, moved.clientY - from.y) < threshold) {
        return;
      }

      moved.preventDefault();
      document.documentElement.classList.add("designer-dragging");
      current = { kind, id, label, x: moved.clientX, y: moved.clientY, dx, dy, target: aim(container.current, kind, id, moved.clientY) };
      setDrag(current);
      scrollNear(container.current, moved.clientY);
    };
    const up = () => {
      const done = current;
      end();

      if (done !== null) {
        swallowClick();

        if (done.target !== null) {
          dropRef.current(done.kind, done.id, done.target);
        }
      }
    };
    const key = (pressed: KeyboardEvent) => {
      if (pressed.key === "Escape") {
        const was = current;
        end();

        if (was !== null) {
          pressed.preventDefault();
          pressed.stopPropagation();
          cancelRef.current?.();
        }
      }
    };
    const end = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      window.removeEventListener("pointercancel", end);
      window.removeEventListener("keydown", key, true);
      document.documentElement.classList.remove("designer-dragging");
      stop.current = null;
      current = null;
      setDrag(null);
    };

    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    window.addEventListener("pointercancel", end);
    window.addEventListener("keydown", key, true);
    stop.current = end;
  }

  /** The marker on an item while something is dragged over it. */
  function mark(kind: DropTarget["kind"], id: string): DropMark | undefined {
    const target = drag?.target;

    if (target === undefined || target === null || target.kind !== kind || target.id !== id) {
      return undefined;
    }

    return target.kind === "page-start" ? "start" : target.after ? "after" : "before";
  }

  return { drag, start, mark, lifted: (id: string) => drag?.id === id };
}

/** The drop place nearest to the pointer: the one under it, or else the closest above or below. */
function aim(root: HTMLElement | null, kind: DragKind, id: string, y: number): DropTarget | null {
  if (root === null) {
    return null;
  }

  const kinds = kind === "page" ? ["page"] : ["question", "page-start"];
  let best: { element: HTMLElement; box: DOMRect; distance: number } | null = null;

  for (const element of root.querySelectorAll<HTMLElement>("[data-drop-kind]")) {
    if (!kinds.includes(element.dataset.dropKind ?? "")) {
      continue;
    }

    const box = element.getBoundingClientRect();
    const distance = y >= box.top && y < box.bottom ? 0 : Math.min(Math.abs(y - box.top), Math.abs(y - box.bottom));

    if (best === null || distance < best.distance) {
      best = { element, box, distance };
    }
  }

  if (best === null) {
    return null;
  }

  const targetId = best.element.dataset.dropId ?? "";
  const targetKind = best.element.dataset.dropKind as DropTarget["kind"];

  if (targetId === id) {
    return null;
  }

  return targetKind === "page-start" ? { kind: "page-start", id: targetId } : { kind: targetKind, id: targetId, after: y > best.box.top + best.box.height / 2 };
}

/** Near the top or bottom of the list, the list scrolls so a far place can be reached. */
function scrollNear(root: HTMLElement | null, y: number) {
  const scroller = root?.querySelector<HTMLElement>("[data-drag-scroll]") ?? root;

  if (scroller === null || scroller === undefined) {
    return;
  }

  const box = scroller.getBoundingClientRect();

  if (box.height === 0) {
    return;
  }

  if (y < box.top + edge) {
    scroller.scrollTop -= 12;
  } else if (y > box.bottom - edge) {
    scroller.scrollTop += 12;
  }
}

/** The click that follows a drag must not also select the item. */
function swallowClick() {
  const swallow = (event: MouseEvent) => {
    event.stopPropagation();
    event.preventDefault();
  };

  window.addEventListener("click", swallow, { capture: true, once: true });
  setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 0);
}

/** The edit a drop makes, or nothing when the item would stay where it is. */
export function dropAction(definition: SurveyDefinition, kind: DragKind, id: string, target: DropTarget): DesignerAction | undefined {
  if (kind === "page") {
    if (target.kind !== "page") {
      return undefined;
    }

    const from = definition.pages.findIndex((page) => page.id === id);
    const onto = definition.pages.findIndex((page) => page.id === target.id);
    const toIndex = onto + (target.after ? 1 : 0) - (from < onto ? 1 : 0);

    return from < 0 || onto < 0 || toIndex === from ? undefined : { type: "movePage", pageId: id, toIndex };
  }

  const fromPage = definition.pages.find((page) => page.questions.some((question) => question.id === id));
  let to: { toPageId: string; toIndex: number } | undefined;

  if (target.kind === "page-start") {
    to = { toPageId: target.id, toIndex: 0 };
  } else if (target.kind === "question") {
    const page = definition.pages.find((each) => each.questions.some((question) => question.id === target.id));

    if (page !== undefined) {
      const rest = page.questions.filter((question) => question.id !== id);
      to = { toPageId: page.id, toIndex: rest.findIndex((question) => question.id === target.id) + (target.after ? 1 : 0) };
    }
  }

  if (fromPage === undefined || to === undefined) {
    return undefined;
  }

  const same = fromPage.id === to.toPageId && fromPage.questions.findIndex((question) => question.id === id) === to.toIndex;

  return same ? undefined : { type: "moveQuestion", questionId: id, ...to };
}

/** Alt+↓ / Alt+↑ on a question: one step within its page, or across to the neighbouring page (W23). */
export function stepQuestion(definition: SurveyDefinition, id: string, down: boolean): DesignerAction | undefined {
  const pageIndex = definition.pages.findIndex((page) => page.questions.some((question) => question.id === id));
  const page = definition.pages[pageIndex];

  if (page === undefined) {
    return undefined;
  }

  const index = page.questions.findIndex((question) => question.id === id);
  const next = definition.pages[pageIndex + 1];
  const previous = definition.pages[pageIndex - 1];

  if (down && index < page.questions.length - 1) {
    return { type: "moveQuestion", questionId: id, toPageId: page.id, toIndex: index + 1 };
  }

  if (down && next !== undefined) {
    return { type: "moveQuestion", questionId: id, toPageId: next.id, toIndex: 0 };
  }

  if (!down && index > 0) {
    return { type: "moveQuestion", questionId: id, toPageId: page.id, toIndex: index - 1 };
  }

  if (!down && previous !== undefined) {
    return { type: "moveQuestion", questionId: id, toPageId: previous.id, toIndex: previous.questions.length };
  }

  return undefined;
}

/** Alt+↓ / Alt+↑ on a page: one place down or up (W23). */
export function stepPage(definition: SurveyDefinition, id: string, down: boolean): DesignerAction | undefined {
  const from = definition.pages.findIndex((page) => page.id === id);
  const toIndex = from + (down ? 1 : -1);

  return from < 0 || toIndex < 0 || toIndex >= definition.pages.length ? undefined : { type: "movePage", pageId: id, toIndex };
}

/** Alt with the up or down arrow: true for down, false for up, undefined for any other key. */
export function altArrow(event: { altKey: boolean; key: string }): boolean | undefined {
  return event.altKey && (event.key === "ArrowDown" || event.key === "ArrowUp") ? event.key === "ArrowDown" : undefined;
}

/** Where an item now is, said once it has moved: "page 2, position 1" or "position 3 of 4". */
export function placeOf(definition: SurveyDefinition, kind: DragKind, id: string): { page: number; position: number; total: number } | undefined {
  if (kind === "page") {
    const index = definition.pages.findIndex((page) => page.id === id);

    return index < 0 ? undefined : { page: index + 1, position: index + 1, total: definition.pages.length };
  }

  for (const [pageIndex, page] of definition.pages.entries()) {
    const index = page.questions.findIndex((question) => question.id === id);

    if (index >= 0) {
      return { page: pageIndex + 1, position: index + 1, total: page.questions.length };
    }
  }

  return undefined;
}

/** The accent line with a dot that marks where a dragged item lands (W34). */
export function DropLine() {
  return <div className="designer-drop" aria-hidden="true" />;
}

/** The lifted copy that follows the pointer: the grip and the item's text, slightly tilted (W34). */
export function Lift({ drag }: { drag: Drag | null }) {
  return drag === null ? null : (
    <div className="designer-lift" aria-hidden="true" style={{ left: drag.x - drag.dx, top: drag.y - drag.dy }}>
      <span className="designer-grip" />
      {drag.label}
    </div>
  );
}

/**
 * Everything the page list and the canvas need to move pages and questions: dragging (above), Alt+↑/↓, focus that
 * stays on the moved item, and a polite live region that says where it went. Movable elements carry
 * `data-move="question:<id>"` or `data-move="page:<id>"` so the focus can follow them.
 */
export function useMoves({ t, state, dispatch }: RegionProps, container: RefObject<HTMLElement | null>) {
  const { definition } = state;
  const [said, setSaid] = useState("");
  const moved = useRef<{ kind: DragKind; id: string; focus: boolean } | null>(null);
  const latest = useRef(definition);
  latest.current = definition;

  function apply(action: DesignerAction | undefined, kind: DragKind, id: string, focus: boolean) {
    if (action !== undefined) {
      moved.current = { kind, id, focus };
      dispatch(action);
    }
  }

  const drag = useDrag(
    container,
    (kind, id, target) => apply(dropAction(latest.current, kind, id, target), kind, id, false),
    () => setSaid(t("moveCancelled")),
  );

  useEffect(() => {
    const done = moved.current;

    if (done === null) {
      return;
    }

    moved.current = null;
    const place = placeOf(definition, done.kind, done.id);

    if (place !== undefined) {
      setSaid(done.kind === "page" ? t("movedPage", { position: place.position, total: place.total }) : t("movedQuestion", { page: place.page, position: place.position }));
    }

    if (done.focus) {
      container.current?.querySelector<HTMLElement>(`[data-move="${done.kind}:${done.id}"]`)?.focus();
    }
  }, [definition, t, container]);

  /** Alt+↑/↓ on a page or a question. */
  function onKeyDown(event: ReactKeyboardEvent<HTMLElement>, kind: DragKind, id: string) {
    const down = altArrow(event);

    if (down === undefined) {
      return;
    }

    event.preventDefault();
    apply(kind === "page" ? stepPage(definition, id, down) : stepQuestion(definition, id, down), kind, id, true);
  }

  const live = (
    <span className="designer-visually-hidden" aria-live="polite">
      {said}
    </span>
  );

  return { ...drag, onKeyDown, live };
}
