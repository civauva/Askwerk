// SPDX-License-Identifier: AGPL-3.0-only
import { ConfirmDialog } from "@askwerk/ui";
import { createTranslator, type Question, type QuestionType } from "@askwerk/schema";
import { memo, useCallback, useRef, useState, type ReactNode } from "react";
import { designerMessages } from "./messages";
import { rulesUsing } from "./logicModel";
import { conditionsText } from "./logicText";
import { allQuestions } from "./state";
import { displayText, languageName, textIn, type RegionProps } from "./text";
import { ImageButton, useImages } from "./images";
import { TypeMenu } from "./TypeMenu";
import { DeletePageDialog, pageName } from "./Outline";

type CanvasProps = RegionProps & {
  typeNames: Record<QuestionType, string>;
  newId: () => string;
  grid: boolean;
  onGridChange: (grid: boolean) => void;
  /** Create → Look (W23): the survey as respondents see it, in place of the pages. */
};

/** Where a new question goes: the page, and the question it follows (null = first on the page). */
type InsertTarget = { pageId: string; afterQuestionId: string | null };

/** Centre region: the survey as respondents see it, edited inline; the grid is optional and off by default (M1, M2). */
export function Canvas({ t, typeNames, state, dispatch, newId, grid, onGridChange }: CanvasProps) {
  const { definition, language } = state;
  const [target, setTarget] = useState<InsertTarget | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const [focusId, setFocusId] = useState<string | null>(null);
  const numbers = new Map(allQuestions(definition).map((question, index) => [question.id, index + 1]));
  // The latest state for the stable "next missing" callback, so cards that did not change need not draw again.
  const latest = useRef(state);
  latest.current = state;
  const onNextMissing = useCallback(
    (questionId: string) => {
      const next = nextMissing(latest.current, questionId);

      if (next === undefined) {
        return false;
      }

      dispatch({ type: "select", questionId: next });
      setFocusId(next);
      return true;
    },
    [dispatch],
  );
  const firstPage = definition.pages[0];
  const isEmpty = allQuestions(definition).length === 0;

  function pick(questionType: QuestionType, extra?: Partial<Question>) {
    if (target === null) {
      return;
    }

    const id = newId();
    dispatch({ type: "addQuestion", questionType, pageId: target.pageId, afterQuestionId: target.afterQuestionId, newId: id, ...(extra !== undefined && { extra }) });
    setFocusId(id);
    setTarget(null);
  }

  const isTarget = (pageId: string, afterQuestionId: string | null) =>
    target !== null && target.pageId === pageId && target.afterQuestionId === afterQuestionId;
  const menu = <TypeMenu t={t} typeNames={typeNames} onPick={pick} onClose={() => setTarget(null)} />;

  return (
    <main className={`designer-canvas${grid ? " designer-canvas--grid" : ""}`} aria-label={t("canvas")}>
      <div className="designer-canvas__scroll">
        {!state.settings && state.edge === "welcome" && <WelcomePage t={t} state={state} dispatch={dispatch} />}
        {!state.settings && state.edge === "end" && <ThankYouPage t={t} state={state} dispatch={dispatch} />}
        {!state.settings && (state.edge === "welcome" || state.edge === "end") ? null : (
          <>
        {!state.settings && isEmpty && firstPage !== undefined && (
          <div className="designer-empty">
            <span className="designer-meta">{t("emptyMeta")}</span>
            <h2>{t("emptyTitle")}</h2>
            <p>{t("emptyText")}</p>
            <div>
              <button
                type="button"
                className="designer-button designer-button--primary"
                onClick={() => setTarget({ pageId: firstPage.id, afterQuestionId: null })}
              >
                {t("addFirst")}
              </button>
            </div>
            {isTarget(firstPage.id, null) && menu}
          </div>
        )}
        {!state.settings &&
          !isEmpty &&
          definition.pages.map((page, pageIndex) => {
            const last = page.questions.at(-1)?.id ?? null;
            const title = displayText(page.title, language, definition);

            return (
              <div key={page.id} className="designer-page">
                {state.selectedPageId === page.id ? (
                  <div className="designer-page__progress designer-page__progress--selected">
                    <span>{t("pageProgress", { number: pageIndex + 1, total: definition.pages.length })}</span>
                    <input
                      className="designer-page__title"
                      data-page-title={page.id}
                      aria-label={t("pageTitle")}
                      placeholder={t("page", { number: pageIndex + 1 })}
                      value={page.title?.[language] ?? ""}
                      onChange={(event) => dispatch({ type: "setPageTitle", pageId: page.id, language, value: event.target.value })}
                    />
                    <span className="designer-page__tools">
                      <button
                        type="button"
                        className="designer-link-button"
                        onClick={() => dispatch({ type: "duplicatePage", pageId: page.id, newId: newId(), questionIds: page.questions.map(() => newId()) })}
                      >
                        {t("pageDuplicate")}
                      </button>
                      {definition.pages.length > 1 && (
                        <button type="button" className="designer-link-button designer-link-button--danger" onClick={() => setDeleting(page.id)}>
                          {t("pageDelete")}
                        </button>
                      )}
                    </span>
                  </div>
                ) : (
                  <button
                    type="button"
                    className="designer-page__progress"
                    aria-label={`${t("pageOf", { number: pageIndex + 1, total: definition.pages.length })}${title !== undefined ? ` · ${title}` : ""}`}
                    onClick={() => dispatch({ type: "selectPage", pageId: page.id })}
                  >
                    <span>
                      {t("pageProgress", { number: pageIndex + 1, total: definition.pages.length })}
                      {title !== undefined && ` · ${title}`}
                    </span>
                    <span>
                      {String(pageIndex + 1).padStart(2, "0")} / {String(definition.pages.length).padStart(2, "0")}
                    </span>
                  </button>
                )}
                <div className="designer-ruler" aria-hidden="true">
                  <i style={{ width: `${((pageIndex + 1) / definition.pages.length) * 100}%` }} />
                </div>
                {page.questions.map((question) => (
                  <div key={question.id} className="designer-page__slot">
                    <QuestionCard
                      t={t}
                      typeNames={typeNames}
                      state={state}
                      dispatch={dispatch}
                      question={question}
                      number={numbers.get(question.id) ?? 0}
                      focus={question.id === focusId}
                      newId={newId}
                      onNextMissing={onNextMissing}
                    />
                    {question.id === state.selectedQuestionId && question.id !== last && (
                      <div className="designer-insert">
                        <button type="button" aria-label={t("insertHere")} onClick={() => setTarget({ pageId: page.id, afterQuestionId: question.id })}>
                          +
                        </button>
                        <span aria-hidden="true">{t("insertHereShort")}</span>
                      </div>
                    )}
                    {question.id !== last && isTarget(page.id, question.id) && menu}
                  </div>
                ))}
                <div className="designer-page__add">
                  <button type="button" className="designer-button designer-button--quiet" onClick={() => setTarget({ pageId: page.id, afterQuestionId: last })}>
                    {t("addQuestion")}
                  </button>
                  {isTarget(page.id, last) && menu}
                </div>
              </div>
            );
          })}
          </>
        )}
        {deleting !== null &&
          (() => {
            const index = definition.pages.findIndex((page) => page.id === deleting);
            const page = definition.pages[index];

            return page === undefined ? null : (
              <DeletePageDialog
                t={t}
                title={pageName(t, page, index, language, definition)}
                questions={page.questions.length}
                onCancel={() => setDeleting(null)}
                onDelete={() => {
                  setDeleting(null);
                  dispatch({ type: "deletePage", pageId: page.id });
                }}
              />
            );
          })()}
      </div>
      <button type="button" role="switch" aria-checked={grid} className="designer-grid-toggle" onClick={() => onGridChange(!grid)}>
        <i aria-hidden="true" />
        {t("grid")}
      </button>
    </main>
  );
}

/** The welcome and thank-you pages as respondents see them, their texts edited in place (AWK-22, W9 A, M5 A). */
/** The welcome page, picked in the page list (W23; was in the settings, W9 A). */
function WelcomePage({ t, state, dispatch }: RegionProps) {
  const { definition, language } = state;
  // Placeholders show what respondents get when a text is left empty: the runner's text in the editing language.
  const standard = createTranslator(designerMessages, language === "de" ? "de" : "en");
  const welcomeOn = definition.welcome?.enabled !== false;
  const text = (field: "welcomeTitle" | "welcomeText", value: string) => dispatch({ type: "setSurveyText", field, language, value });

  return (
    <div className="designer-page">
      <section className={`designer-welcome${welcomeOn ? "" : " designer-welcome--off"}`} aria-label={t("welcomePage")}>
        <div className="designer-welcome__kick">
          <span>{t("welcomeKick")}</span>
          <span>{language.toUpperCase()}</span>
        </div>
        {welcomeOn ? (
          <>
            <input
              className="designer-welcome__title"
              aria-label={t("welcomeTitle")}
              placeholder={standard("welcomeTitlePlaceholder")}
              value={definition.welcome?.title?.[language] ?? ""}
              onChange={(event) => text("welcomeTitle", event.target.value)}
            />
            <textarea
              className="designer-welcome__text"
              aria-label={t("welcomeText")}
              placeholder={standard("welcomeTextPlaceholder")}
              rows={2}
              value={definition.welcome?.text?.[language] ?? ""}
              onChange={(event) => text("welcomeText", event.target.value)}
            />
            <div>
              <span className="designer-welcome__start" aria-hidden="true">
                {standard("welcomeStart")}
              </span>
            </div>
          </>
        ) : (
          <p className="designer-note">{t("welcomeOff")}</p>
        )}
      </section>
    </div>
  );
}

/** The thank-you page, picked in the page list (W23). */
function ThankYouPage({ t, state, dispatch }: RegionProps) {
  const { definition, language } = state;
  const standard = createTranslator(designerMessages, language === "de" ? "de" : "en");

  return (
    <div className="designer-page">
      <section className="designer-thanks" aria-label={t("endKick")}>
        <span className="designer-welcome__kick">{t("endKick")}</span>
        <input
          className="designer-thanks__text"
          aria-label={t("endText")}
          placeholder={standard("endDefault")}
          value={definition.endPage?.text[language] ?? ""}
          onChange={(event) => dispatch({ type: "setSurveyText", field: "endText", language, value: event.target.value })}
        />
        <p className="designer-note">{t("endNote")}</p>
      </section>
    </div>
  );
}

type QuestionCardProps = RegionProps & {
  typeNames: Record<QuestionType, string>;
  question: Question;
  number: number;
  focus: boolean;
  newId: () => string;
  /** Selects the next question after this one whose text is missing in the editing language; false when there is none. */
  onNextMissing: (questionId: string) => boolean;
};

/** The next question (wrapping around) whose text is missing in the editing language but exists in the default language. */
function nextMissing(state: CanvasProps["state"], currentId: string): string | undefined {
  const { definition, language } = state;

  if (language === definition.defaultLanguage) {
    return undefined;
  }

  const questions = allQuestions(definition);
  const start = questions.findIndex((question) => question.id === currentId);
  const ordered = [...questions.slice(start + 1), ...questions.slice(0, start)];

  return ordered.find((question) => textIn(question.text, language) === undefined && textIn(question.text, definition.defaultLanguage) !== undefined)?.id;
}

/** A card draws again only when its question, number, selection, focus or the editing language changed. */
const QuestionCard = memo(QuestionCardView, (before, after) => {
  const selected = (props: QuestionCardProps) => props.state.selectedQuestionId === props.question.id;

  return (
    before.question === after.question &&
    before.number === after.number &&
    before.focus === after.focus &&
    before.t === after.t &&
    before.typeNames === after.typeNames &&
    before.onNextMissing === after.onNextMissing &&
    selected(before) === selected(after) &&
    before.state.language === after.state.language &&
    before.state.definition.languages === after.state.definition.languages &&
    before.state.definition.defaultLanguage === after.state.definition.defaultLanguage
  );
});

function QuestionCardView({ t, typeNames, state, dispatch, question, number, focus, newId, onNextMissing }: QuestionCardProps) {
  const { definition, language } = state;
  const selected = question.id === state.selectedQuestionId;
  const own = textIn(question.text, language);
  const source = textIn(question.text, definition.defaultLanguage);
  const untranslated = own === undefined && source !== undefined && language !== definition.defaultLanguage;
  const isBlock = question.type === "text-block";
  const isImage = question.type === "image";
  const translating = language !== definition.defaultLanguage;
  const label = `${isBlock ? t("blockLabel") : isImage ? t("imageLabel") : `Q${number}`} · ${question.variable}`;
  const [deleting, setDeleting] = useState(false);
  const uses = rulesUsing(definition, question.id);

  return (
    <article
      className={`designer-card${selected ? " designer-card--selected" : ""}${untranslated ? " designer-card--missing" : ""}`}
      aria-label={label}
      onClick={() => !selected && dispatch({ type: "select", questionId: question.id })}
    >
      <div className="designer-card__number">
        <span>{label}</span>
        {untranslated && <span className="designer-badge">{t("translationMissing")}</span>}
        {!untranslated && question.required && <span>★ {t("required")}</span>}
      </div>
      {question.showWhen !== undefined && <p className="designer-logic__badge">{t("logicBadge", { conditions: conditionsText(t, question.showWhen, definition, language) })}</p>}
      {untranslated && <p className="designer-card__source">{t("sourceText", { language: languageName(t, definition.defaultLanguage), text: source })}</p>}
      {isImage ? (
        <ImageCardBody t={t} state={state} dispatch={dispatch} question={question} selected={selected} />
      ) : selected ? (
        isBlock ? (
          <textarea
            className="designer-card__block-input"
            aria-label={t("blockText")}
            autoFocus={focus}
            rows={3}
            value={own ?? ""}
            onChange={(event) => dispatch({ type: "setText", questionId: question.id, field: "text", language, value: event.target.value })}
          />
        ) : (
        <>
          <input
            className="designer-card__title-input"
            aria-label={t("questionText")}
            placeholder={t("questionTextIn", { language: languageName(t, language) })}
            autoFocus={focus}
            onKeyDown={(event) => {
              if (event.key === "Tab" && !event.shiftKey && translating && onNextMissing(question.id)) {
                event.preventDefault();
              }
            }}
            value={own ?? ""}
            onChange={(event) => dispatch({ type: "setText", questionId: question.id, field: "text", language, value: event.target.value })}
          />
          <input
            className="designer-card__help-input"
            aria-label={t("helpText")}
            placeholder={t("helpPlaceholder")}
            value={textIn(question.help, language) ?? ""}
            onChange={(event) => dispatch({ type: "setText", questionId: question.id, field: "help", language, value: event.target.value })}
          />
        </>
        )
      ) : isBlock ? (
        !untranslated && <p className="designer-card__block">{own}</p>
      ) : (
        !untranslated && (
          <>
            <h3 className="designer-card__title">
              {own ?? t("untitled")}
              {question.required && (
                <span className="designer-card__required" aria-hidden="true">
                  {" *"}
                </span>
              )}
            </h3>
            {textIn(question.help, language) !== undefined && <p className="designer-card__help">{textIn(question.help, language)}</p>}
          </>
        )
      )}
      {!isBlock && !isImage && question.image !== undefined && <QuestionImagePreview question={question} language={language} definition={definition} />}
      {!isBlock && !isImage && <AnswerPreview t={t} state={state} dispatch={dispatch} question={question} />}
      {selected && (
        <div className="designer-card__footer">
          <span>
            {typeNames[question.type].toUpperCase()} · {question.variable}
            {question.required && ` · ${t("required").toUpperCase()}`}
          </span>
          {untranslated && <span className="designer-card__hint">{t("nextMissing")}</span>}
          <span className="designer-card__actions">
            <button type="button" onClick={() => dispatch({ type: "duplicateQuestion", questionId: question.id, newId: newId() })}>
              {t("duplicate")}
            </button>
            <span aria-hidden="true"> · </span>
            <button type="button" onClick={() => (rulesUsing(definition, question.id) > 0 ? setDeleting(true) : dispatch({ type: "deleteQuestion", questionId: question.id }))}>
              {t("delete")}
            </button>
          </span>
        </div>
      )}
      {deleting && (
        <ConfirmDialog
          title={t("deleteQuestionTitle")}
          text={uses === 1 ? t("deleteQuestionUsedOne", { variable: question.variable }) : t("deleteQuestionUsed", { variable: question.variable, count: uses })}
          cancel={t("cancel")}
          confirm={t("delete")}
          onCancel={() => setDeleting(false)}
          onConfirm={() => {
            setDeleting(false);
            dispatch({ type: "deleteQuestion", questionId: question.id });
          }}
        />
      )}
    </article>
  );
}


const faces = ["☹", "🙁", "😐", "🙂", "😀"];

/** A still picture of the answer area, so the canvas looks like the survey. */
function AnswerPreview({ t, state, question }: RegionProps & { question: Question }) {
  const { definition, language } = state;
  const text = (value: Question["text"] | undefined) => displayText(value, language, definition);
  const ends = (
    <div className="designer-scale__ends">
      <span>{text(question.scale?.minLabel)}</span>
      <span>{text(question.scale?.maxLabel)}</span>
    </div>
  );

  switch (question.type) {
    case "rating":
    case "nps": {
      const min = question.type === "nps" ? 0 : (question.scale?.min ?? 1);
      const max = question.type === "nps" ? 10 : (question.scale?.max ?? 5);
      const steps = Array.from({ length: Math.max(0, max - min + 1) }, (_, index) => min + index);
      const display = question.type === "rating" ? (question.display ?? "numbers") : "numbers";
      const symbol = (step: number, index: number) =>
        display === "stars" ? "★" : display === "dots" ? <i className="designer-scale__dot" style={{ width: 10 + index * 2, height: 10 + index * 2 }} /> : display === "faces" ? (steps.length === 3 ? [faces[0], faces[2], faces[4]][index] : faces[index]) : step;

      return (
        <div aria-hidden="true">
          <div className={`designer-scale designer-scale--${display}`} style={{ gridTemplateColumns: `repeat(${steps.length}, 1fr)` }}>
            {steps.map((step, index) => (
              <span key={step}>{symbol(step, index)}</span>
            ))}
          </div>
          {ends}
        </div>
      );
    }
    case "slider":
      return (
        <div aria-hidden="true" className="designer-slider">
          <span className="designer-meta">{t("notAnswered")}</span>
          <i className="designer-slider__bar" />
          <div className="designer-scale__ends">
            <span>
              {question.scale?.min ?? 0} {text(question.scale?.minLabel)}
            </span>
            <span>
              {text(question.scale?.maxLabel)} {question.scale?.max ?? 100}
            </span>
          </div>
        </div>
      );
    case "yes-no":
      return (
        <div aria-hidden="true" className="designer-yesno">
          <span>{text(question.yes?.label) ?? t("yes")}</span>
          <span>{text(question.no?.label) ?? t("no")}</span>
        </div>
      );
    case "dropdown":
      return (
        <div aria-hidden="true" className="designer-answer designer-answer--select">
          <span>{t("choose")}</span>
          <span>▾</span>
        </div>
      );
    case "single":
    case "multiple":
    case "ranking":
      return (
        <ul className="designer-options" aria-hidden="true">
          {question.options?.map((option) => (
            <li key={option.code}>
              <i className={question.type === "single" ? "designer-options__round" : ""} />
              {text(option.label)}
            </li>
          ))}
          {question.other !== undefined && (
            <li>
              <i className={question.type === "single" ? "designer-options__round" : ""} />
              {text(question.other.label)}
              <span className="designer-answer designer-answer--inline" />
            </li>
          )}
        </ul>
      );
    case "date": {
      const mode = question.mode ?? "date";

      return (
        <div aria-hidden="true" className="designer-yesno designer-datefields">
          {mode !== "time" && <span className="designer-answer">{t("datePattern")}</span>}
          {mode !== "date" && <span className="designer-answer">{t("timePattern")}</span>}
        </div>
      );
    }
    case "long-text":
      return (
        <div className="designer-answer designer-answer--long" aria-hidden="true">
          {t("yourAnswer")}
        </div>
      );
    default:
      return (
        <div className="designer-answer" aria-hidden="true">
          {text(question.placeholder) ?? t("yourAnswer")}
        </div>
      );
  }
}

/** An image block on the canvas: the image as respondents see it, the caption edited in place (W10 F1). */
function ImageCardBody({ t, state, dispatch, question, selected }: RegionProps & { question: Question; selected: boolean }) {
  const { url } = useImages();
  const { definition, language } = state;
  const image = question.image;
  const caption = textIn(question.text, language);
  const set = (asset: string) => dispatch({ type: "patch", questionId: question.id, patch: { image: { ...image, asset } } });

  return (
    <div className="designer-image-block">
      {image !== undefined && image.asset !== "" ? (
        <img src={url(image.asset, 960)} alt={displayText(image.alt, language, definition) ?? ""} />
      ) : (
        <p className="designer-note">{t("imageEmpty")}</p>
      )}
      <ImageButton label={image === undefined ? t("uploadImage") : t("replaceImage")} fileLabel={t("imageFile")} onImage={set} />
      {selected ? (
        <input
          className="designer-card__help-input"
          aria-label={t("caption")}
          placeholder={t("captionPlaceholder")}
          value={caption ?? ""}
          onChange={(event) => dispatch({ type: "setText", questionId: question.id, field: "text", language, value: event.target.value })}
        />
      ) : (
        caption !== undefined && <p className="designer-card__help">{caption}</p>
      )}
    </div>
  );
}

/** The image that goes with a question, small, as on the respondent's page. */
function QuestionImagePreview({ question, language, definition }: { question: Question; language: string; definition: RegionProps["state"]["definition"] }) {
  const { url } = useImages();
  const image = question.image!;

  return image.asset === "" ? null : <img className="designer-question-image" src={url(image.asset, 480)} alt={displayText(image.alt, language, definition) ?? ""} />;
}
