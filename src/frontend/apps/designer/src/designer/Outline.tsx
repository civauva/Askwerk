// SPDX-License-Identifier: AGPL-3.0-only
import type { Page, Question } from "@askwerk/schema";
import { ConfirmDialog } from "@askwerk/ui";
import { useEffect, useRef, useState } from "react";
import { DropLine, Lift, useMoves } from "./drag";
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
  const list = useRef<HTMLElement>(null);
  // Drag or Alt+↑/↓ to move pages and questions (W34); the moved item keeps the focus and the move is announced.
  const moves = useMoves({ t, state, dispatch }, list);
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

  const welcomeOn = definition.welcome?.enabled !== false;
  const rules = definition.pages.reduce((count, page) => count + (page.showWhen === undefined ? 0 : 1) + (page.after === undefined ? 0 : 1) + page.questions.filter((question) => question.showWhen !== undefined).length, 0);

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

        const number = String(pageIndex + 1).padStart(2, "0");
        const name = pageName(t, page, pageIndex, language, definition);

        return (
          <section key={page.id} className="designer-outline__page" aria-labelledby={`outline-page-${page.id}`} data-drop-kind="page" data-drop-id={page.id}>
            {moves.mark("page", page.id) === "before" && <DropLine />}
            <h2 id={`outline-page-${page.id}`} className="designer-outline__page-title" data-drop-kind="page-start" data-drop-id={page.id}>
              <button
                type="button"
                className="designer-outline__page"
                data-move={`page:${page.id}`}
                data-lifted={moves.lifted(page.id) ? "true" : undefined}
                aria-current={state.selectedPageId === page.id ? "true" : undefined}
                aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"
                onClick={() => dispatch({ type: "selectPage", pageId: page.id })}
                onKeyDown={(event) => moves.onKeyDown(event, "page", page.id)}
                onPointerDown={(event) => moves.start(event, "page", page.id, `${number} · ${name}`)}
              >
                <span className="designer-grip" data-grip aria-hidden="true" />
                {number} · {name}
              </button>
              <span>{page.questions.length}</span>
            </h2>
            {moves.mark("page-start", page.id) === "start" && <DropLine />}
            <ul>
              {shown.map((question) => (
                <li key={question.id} data-drop-kind="question" data-drop-id={question.id}>
                  {moves.mark("question", question.id) === "before" && <DropLine />}
                  <button
                    type="button"
                    className="designer-outline__question"
                    data-question-id={question.id}
                    data-move={`question:${question.id}`}
                    data-lifted={moves.lifted(question.id) ? "true" : undefined}
                    aria-current={question.id === selectedQuestionId ? "true" : undefined}
                    aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"
                    onClick={() => dispatch({ type: "select", questionId: question.id })}
                    onKeyDown={(event) => moves.onKeyDown(event, "question", question.id)}
                    onPointerDown={(event) => moves.start(event, "question", question.id, displayText(question.text, language, definition) ?? t("untitled"))}
                  >
                    <span className="designer-grip" data-grip aria-hidden="true" />
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
                  {moves.mark("question", question.id) === "after" && <DropLine />}
                </li>
              ))}
            </ul>
            {moves.mark("page", page.id) === "after" && <DropLine />}
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
      <Lift drag={moves.drag} />
      {moves.live}
    </nav>
  );
}
