// SPDX-License-Identifier: AGPL-3.0-only
import type { Page, Question } from "@askwerk/schema";
import { ConfirmDialog } from "@askwerk/ui";
import { useEffect, useRef, useState, type KeyboardEvent, type DragEvent } from "react";
import { displayText, textIn, type RegionProps } from "./text";

/** A page's title in the language shown, or "Page n" when it has none. */
export function pageName(t: RegionProps["t"], page: Page, index: number, language: string, definition: RegionProps["state"]["definition"]): string {
  return displayText(page.title, language, definition) ?? t("page", { number: index + 1 });
}

/** Asks before a page goes, naming how many questions go with it (W17 A). */
export function DeletePageDialog({ t, title, questions, onDelete, onCancel }: { t: RegionProps["t"]; title: string; questions: number; onDelete: () => void; onCancel: () => void }) {
  return (
    <ConfirmDialog
      title={t("deletePageTitle")}
      text={questions === 0 ? t("deletePageAskEmpty", { title }) : questions === 1 ? t("deletePageAskOne", { title }) : t("deletePageAsk", { title, count: questions })}
      confirm={t("delete")}
      cancel={t("cancel")}
      onConfirm={onDelete}
      onCancel={onCancel}
    />
  );
}

type OutlineProps = RegionProps & { newId: () => string };

/**
 * Left region: search (/), the welcome page, the pages and their questions with variable names, the thank-you page,
 * and all rules (W23). Drag or Alt+↑↓ to move questions and pages (M1, W23).
 */
export function Outline({ t, state, dispatch, newId }: OutlineProps) {
  const { definition, language, selectedQuestionId } = state;
  const [query, setQuery] = useState("");
  const search = useRef<HTMLInputElement>(null);
  const dragging = useRef<string | null>(null);
  const draggingPage = useRef<string | null>(null);
  // Where a dragged item would land (W34): before or after the item under the pointer; the dragged one fades.
  const [over, setOver] = useState<{ id: string; after: boolean } | null>(null);
  const [lifted, setLifted] = useState<string | null>(null);
  const endDrag = () => {
    setOver(null);
    setLifted(null);
  };
  const aim = (event: DragEvent<HTMLElement>, id: string) => {
    event.preventDefault();
    const box = event.currentTarget.getBoundingClientRect();
    const after = event.clientY > box.top + box.height / 2;

    if (over?.id !== id || over.after !== after) {
      setOver({ id, after });
    }
  };
  const dropMark = (id: string) => (over?.id === id ? (over.after ? "after" : "before") : undefined);
  const refocus = useRef<string | null>(null);
  const refocusPage = useRef<string | null>(null);
  const list = useRef<HTMLElement>(null);
  const needle = query.trim().toLowerCase();
  const matches = (question: Question) =>
    needle === "" ||
    question.variable.toLowerCase().includes(needle) ||
    (displayText(question.text, language, definition) ?? "").toLowerCase().includes(needle);
  const anyMatch = definition.pages.some((page) => page.questions.some(matches));

  useEffect(() => {
    function onKeyDown(event: globalThis.KeyboardEvent) {
      const target = event.target as HTMLElement | null;
      const typing = target !== null && (["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName) || target.isContentEditable);

      if (event.key === "/" && !typing) {
        event.preventDefault();
        search.current?.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown);

    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  useEffect(() => {
    if (refocus.current !== null) {
      list.current?.querySelector<HTMLButtonElement>(`[data-question-id="${refocus.current}"]`)?.focus();
      refocus.current = null;
    }

    if (refocusPage.current !== null) {
      list.current?.querySelector<HTMLButtonElement>(`[data-page-id="${refocusPage.current}"]`)?.focus();
      refocusPage.current = null;
    }
  });

  /** Alt+↓ / Alt+↑ on a page: one place down or up (W23), so pages move without dragging. */
  function onPageKeyDown(event: KeyboardEvent<HTMLButtonElement>, pageIndex: number, pageId: string) {
    if (!event.altKey || (event.key !== "ArrowDown" && event.key !== "ArrowUp")) {
      return;
    }

    event.preventDefault();
    const toIndex = pageIndex + (event.key === "ArrowDown" ? 1 : -1);

    if (toIndex >= 0 && toIndex < definition.pages.length) {
      refocusPage.current = pageId;
      dispatch({ type: "movePage", pageId, toIndex });
    }
  }

  const welcomeOn = definition.welcome?.enabled !== false;
  const rules = definition.pages.reduce((count, page) => count + (page.showWhen === undefined ? 0 : 1) + (page.after === undefined ? 0 : 1) + page.questions.filter((question) => question.showWhen !== undefined).length, 0);

  /** Moves before or after the target question, on the target's page. */
  function dropOn(target: Question, after: boolean) {
    const moving = dragging.current;
    dragging.current = null;
    endDrag();
    const page = definition.pages.find((each) => each.questions.some((question) => question.id === target.id));

    if (moving === null || moving === target.id || page === undefined) {
      return;
    }

    const toIndex = page.questions.filter((question) => question.id !== moving).findIndex((question) => question.id === target.id) + (after ? 1 : 0);
    dispatch({ type: "moveQuestion", questionId: moving, toPageId: page.id, toIndex });
  }

  /** Alt+↓ / Alt+↑: one step within the page, or across to the neighbouring page. */
  function onQuestionKeyDown(event: KeyboardEvent<HTMLButtonElement>, question: Question) {
    if (!event.altKey || (event.key !== "ArrowDown" && event.key !== "ArrowUp")) {
      return;
    }

    event.preventDefault();
    const pageIndex = definition.pages.findIndex((page) => page.questions.includes(question));
    const page = definition.pages[pageIndex]!;
    const index = page.questions.indexOf(question);
    const down = event.key === "ArrowDown";
    const next = definition.pages[pageIndex + 1];
    const previous = definition.pages[pageIndex - 1];
    let move: { toPageId: string; toIndex: number } | undefined;

    if (down && index < page.questions.length - 1) {
      move = { toPageId: page.id, toIndex: index + 1 };
    } else if (down && next !== undefined) {
      move = { toPageId: next.id, toIndex: 0 };
    } else if (!down && index > 0) {
      move = { toPageId: page.id, toIndex: index - 1 };
    } else if (!down && previous !== undefined) {
      move = { toPageId: previous.id, toIndex: previous.questions.length };
    }

    if (move !== undefined) {
      refocus.current = question.id;
      dispatch({ type: "moveQuestion", questionId: question.id, ...move });
    }
  }

  return (
    <nav ref={list} className="designer-outline" aria-label={t("outline")}>
      <div className="designer-outline__search">
        <input ref={search} type="search" aria-label={t("searchQuestions")} placeholder={t("searchQuestions")} value={query} onChange={(event) => setQuery(event.target.value)} />
        <kbd className="designer-kbd" aria-hidden="true">
          /
        </kbd>
      </div>
      {!anyMatch && <p className="designer-note">{t("noMatches")}</p>}
      <button type="button" className="designer-outline__edge" aria-current={state.edge === "welcome" ? "true" : undefined} onClick={() => dispatch({ type: "selectEdge", edge: "welcome" })}>
        <span>{t("welcomePage")}</span>
        <span className="designer-outline__edge-state">{welcomeOn ? t("outlineOn") : t("outlineOff")}</span>
      </button>
      {definition.pages.map((page, pageIndex) => {
        const shown = page.questions.filter(matches);

        if (needle !== "" && shown.length === 0) {
          return null;
        }

        return (
          <section key={page.id} className="designer-outline__page" aria-labelledby={`outline-page-${page.id}`}>
            <h2 id={`outline-page-${page.id}`} className="designer-outline__page-title">
              <button
                type="button"
                className="designer-outline__page"
                data-page-id={page.id}
                draggable
                data-drop={dropMark(page.id)}
                data-lifted={lifted === page.id ? "true" : undefined}
                aria-current={state.selectedPageId === page.id ? "true" : undefined}
                aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"
                onClick={() => dispatch({ type: "selectPage", pageId: page.id })}
                onKeyDown={(event) => onPageKeyDown(event, pageIndex, page.id)}
                onDragStart={() => {
                  draggingPage.current = page.id;
                  setLifted(page.id);
                }}
                onDragOver={(event) => draggingPage.current !== null && aim(event, page.id)}
                onDragEnd={endDrag}
                onDrop={(event) => {
                  event.preventDefault();
                  const moving = draggingPage.current;
                  const after = over?.after === true;
                  draggingPage.current = null;
                  endDrag();

                  if (moving !== null && moving !== page.id) {
                    const from = definition.pages.findIndex((each) => each.id === moving);
                    const target = pageIndex + (after ? 1 : 0) - (from < pageIndex ? 1 : 0);
                    dispatch({ type: "movePage", pageId: moving, toIndex: target });
                  }
                }}
              >
                {String(pageIndex + 1).padStart(2, "0")} · {pageName(t, page, pageIndex, language, definition)}
              </button>
              <span>{page.questions.length}</span>
            </h2>
            <ul>
              {shown.map((question) => (
                <li key={question.id}>
                  <button
                    type="button"
                    className="designer-outline__question"
                    data-question-id={question.id}
                    draggable
                    data-drop={dropMark(question.id)}
                    data-lifted={lifted === question.id ? "true" : undefined}
                    aria-current={question.id === selectedQuestionId ? "true" : undefined}
                    onClick={() => dispatch({ type: "select", questionId: question.id })}
                    onKeyDown={(event) => onQuestionKeyDown(event, question)}
                    onDragStart={() => {
                      dragging.current = question.id;
                      setLifted(question.id);
                    }}
                    onDragOver={(event) => dragging.current !== null && aim(event, question.id)}
                    onDragEnd={endDrag}
                    onDrop={(event) => {
                      event.preventDefault();
                      dropOn(question, over?.after === true);
                    }}
                  >
                    <span className="designer-outline__grip" aria-hidden="true" />
                    <span className="designer-outline__label">
                      {displayText(question.text, language, definition) ?? t("untitled")}
                      {textIn(question.text, language) === undefined && textIn(question.text, definition.defaultLanguage) !== undefined && (
                        <>
                          {" "}
                          <i className="designer-outline__missing" role="img" aria-label={t("translationMissing")} />
                        </>
                      )}
                    </span>{" "}
                    {question.key === true && (
                      <>
                        <span className="designer-key" role="img" aria-label={t("keyQuestion")}>
                          ★
                        </span>{" "}
                      </>
                    )}
                    <code>
                      {question.showWhen !== undefined && <span aria-hidden="true">⑂ </span>}
                      {question.variable}
                    </code>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
      <button type="button" className="designer-outline__add" onClick={() => dispatch({ type: "addPage", newId: newId() })}>
        {t("addPage")}
      </button>
      <button type="button" className="designer-outline__edge" aria-current={state.edge === "end" ? "true" : undefined} onClick={() => dispatch({ type: "selectEdge", edge: "end" })}>
        <span>{t("thankYouPage")}</span>
      </button>
      <button type="button" className="designer-outline__rules" aria-current={state.edge === "rules" ? "true" : undefined} onClick={() => dispatch({ type: "selectEdge", edge: "rules" })}>
        <span>{t("allRules")}</span>
        <span className="designer-outline__edge-state">{rules}</span>
      </button>
    </nav>
  );
}
