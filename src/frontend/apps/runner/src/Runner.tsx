// SPDX-License-Identifier: AGPL-3.0-only
import "./runner.css";
import { ApiError, type AnswerValue, type Answers, type RunClient, type RunResponse } from "@askwerk/app-client";
import { createTranslator, route, type Question, type SurveyDefinition, type ThemeTokens, type TranslatedText } from "@askwerk/schema";
import { hostLookVariables, type HostLook, LanguageMenu } from "@askwerk/ui";
import { createContext, useCallback, useContext, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { runnerMessages } from "./messages";
import { AssetUrl, QuestionField, textOf } from "./QuestionField";
import { themeLook, ThemeFonts, usePrefersDark, type Look } from "./theme";

type RunnerProps = {
  client: RunClient;
  surveyId: string;
  /** The browser's language; the survey's default language when the survey does not offer it. */
  initialLocale: string;
  /** Skip the short pause before moving on (prefers-reduced-motion; W8 note 4). */
  reducedMotion?: boolean;
  /** The editor's preview of the draft (AWK-22, W9): a banner on every screen says that nothing is saved. */
  preview?: boolean;
  /** The preview inside the designer's panel (W26): takes the look being edited and goes where it shows. */
  liveLook?: boolean;
  /** An answer clicked in the invitation email (AWK-17 step 6): the survey opens with it chosen, past the welcome page. */
  initialAnswer?: { questionId: string; value: string };
};

/** Whether this is the preview; every Frame then shows the banner. */
const PreviewMode = createContext(false);

/** The survey's look (AWK-17); null until the survey is loaded, and for the Werkstatt look. */
const LookContext = createContext<Look | null>(null);

/** The host app's look from the frame's address (W37, <askwerk-survey look="host">); it wins over the survey's look. */
export const HostStyle = createContext<HostLook | null>(null);

/** The organisation's saved host look, under the one from the address. */
const SavedHostLook = createContext<HostLook | null>(null);

type Screen =
  | { kind: "loading" }
  | { kind: "not-found" }
  | { kind: "closed"; closedAt?: string | undefined }
  | { kind: "already" }
  | { kind: "failed"; traceId: string | undefined; retry: () => void }
  | { kind: "welcome" }
  | { kind: "welcome-back"; response: RunResponse }
  | { kind: "answering"; page: number; step: number }
  | { kind: "done" };

type Errors = Readonly<Record<string, string>>;

/** The runner (AWK-6, W8 rev. 2, M4): welcome, the classic or one-at-a-time layout, errors, the end and other screens. */
export function Runner({ preview = false, ...props }: RunnerProps) {
  return (
    <PreviewMode.Provider value={preview}>
      <RunnerScreens {...props} />
    </PreviewMode.Provider>
  );
}

function RunnerScreens(props: Omit<RunnerProps, "preview">) {
  const [look, setLook] = useState<Look | null>(null);
  const [savedHost, setSavedHost] = useState<HostLook | null>(null);
  const { client, surveyId } = props;
  const preview = props.liveLook === true;
  const ownHeader = useRef<SurveyDefinition["headerImage"]>(undefined);
  // Stable, so loading the survey does not start again when the look is set.
  const onTheme = useCallback(
    (theme: ThemeTokens | undefined, own?: SurveyDefinition["headerImage"], host?: HostLook | null) => {
      ownHeader.current = own;
      host !== undefined && setSavedHost(host);
      setLook(theme === undefined ? null : themeLook(theme, (assetId, width) => client.assetUrl(surveyId, assetId, width), own));
    },
    [client, surveyId],
  );

  // The preview beside Look (W26) takes the look being edited at once, from the designer on the same origin only.
  useEffect(() => {
    if (!preview) {
      return undefined;
    }

    const received = (event: MessageEvent) => {
      const data = event.data as { type?: string; tokens?: ThemeTokens } | null;

      if (event.origin === window.location.origin && data?.type === "askwerk:look" && data.tokens !== undefined) {
        setLook(themeLook(data.tokens, (assetId, width) => client.assetUrl(surveyId, assetId, width), ownHeader.current));
      }
    };
    window.addEventListener("message", received);

    return () => window.removeEventListener("message", received);
  }, [preview, client, surveyId]);

  const assetUrl = useCallback((assetId: string, width?: number) => client.assetUrl(surveyId, assetId, width), [client, surveyId]);

  return (
    <LookContext.Provider value={look}>
      <SavedHostLook.Provider value={savedHost}>
      <AssetUrl.Provider value={assetUrl}>
        {look !== null && <ThemeFonts look={look} />}
        <SurveyScreens {...props} onTheme={onTheme} />
      </AssetUrl.Provider>
      </SavedHostLook.Provider>
    </LookContext.Provider>
  );
}

function SurveyScreens({ client, surveyId, initialLocale, reducedMotion = false, initialAnswer, liveLook, onTheme }: Omit<RunnerProps, "preview"> & { onTheme: (theme: ThemeTokens | undefined, own?: SurveyDefinition["headerImage"], host?: HostLook | null) => void }) {
  const preview = liveLook === true;
  const [definition, setDefinition] = useState<SurveyDefinition | null>(null);
  const [organisation, setOrganisation] = useState("");
  // Collecting settings (W24): whether a browser that finished may answer again, and the time zone dates are shown in.
  const [oneResponsePerPerson, setOneResponsePerPerson] = useState(true);
  const [timeZone, setTimeZone] = useState<string | undefined>(undefined);
  const [language, setLanguage] = useState(initialLocale);
  const [screen, setScreen] = useState<Screen>({ kind: "loading" });
  const [values, setValues] = useState<Record<string, AnswerValue>>({});
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState(false);
  const summary = useRef<HTMLDivElement>(null);
  // With the welcome page switched off, the response starts with the first page saved, not on opening the link.
  const started = useRef(false);
  const uiLanguage = language === "de" ? "de" : "en";
  const t = createTranslator(runnerMessages, uiLanguage);

  const failed = useCallback(
    (error: unknown, retry: () => void) => setScreen({ kind: "failed", traceId: error instanceof ApiError ? error.traceId : undefined, retry }),
    [],
  );

  const load = useCallback(() => {
    setScreen({ kind: "loading" });
    client.open<SurveyDefinition>(surveyId).then((opened) => {
      if (opened === null) {
        setScreen({ kind: "not-found" });
        return;
      }

      const survey = opened.survey.definition;
      onTheme(opened.survey.theme as ThemeTokens | undefined, survey.headerImage, hostLookOf(opened.hostLook));

      // The designer's Look sends the look being edited once the preview has its survey (W26).
      if (preview && window.parent !== window) {
        window.parent.postMessage({ type: "askwerk:preview-ready" }, window.location.origin);
      }
      setDefinition(survey);
      setOrganisation(opened.survey.organisation);
      setOneResponsePerPerson(opened.survey.oneResponsePerPerson !== false);
      setTimeZone(opened.survey.timeZone);
      setLanguage((current) => opened.response?.language ?? (survey.languages.includes(current) ? current : survey.defaultLanguage));

      if (opened.response?.status === "complete") {
        setScreen({ kind: opened.survey.oneResponsePerPerson === false ? "done" : "already" });
      } else if (opened.response) {
        started.current = true;
        setValues({ ...opened.response.answers });
        setScreen({ kind: "welcome-back", response: opened.response });
      } else if (opened.survey.state === "closed") {
        setScreen({ kind: "closed", closedAt: opened.survey.closedAt ?? undefined });
      } else if (initialAnswer !== undefined && answerFromEmail(survey, initialAnswer) !== undefined) {
        const { question, value } = answerFromEmail(survey, initialAnswer)!;
        const page = Math.max(0, survey.pages.findIndex((each) => each.questions.includes(question)));
        setValues({ [question.id]: value });
        client.start(surveyId, survey.languages.includes(initialLocale) ? initialLocale : survey.defaultLanguage).then(
          () => {
            started.current = true;
            setScreen({ kind: "answering", page, step: Math.max(0, survey.pages.slice(0, page).reduce((count, each) => count + each.questions.length, 0) + survey.pages[page]!.questions.indexOf(question)) });
          },
          (error: unknown) => failed(error, load),
        );
      } else if (survey.welcome?.enabled === false) {
        setScreen({ kind: "answering", page: 0, step: 0 });
      } else {
        setScreen({ kind: "welcome" });
      }
    }, (error: unknown) => failed(error, load));
  }, [client, surveyId, failed, onTheme, initialAnswer, initialLocale, preview]);

  useEffect(load, [load]);

  // The preview beside Look (W26) goes where the look being changed shows: welcome, end, or a page with that question type.

  useEffect(() => {
    if (!preview || definition === null) {
      return undefined;
    }

    const received = (event: MessageEvent) => {
      const data = event.data as { type?: string; target?: string } | null;

      if (event.origin !== window.location.origin || data?.type !== "askwerk:goto" || typeof data.target !== "string") {
        return;
      }

      if (data.target === "welcome" || data.target === "end") {
        setScreen({ kind: data.target === "welcome" ? "welcome" : "done" });
        return;
      }

      const types = data.target.split(",");
      const page = definition.pages.findIndex((each) => each.questions.some((question) => types.includes(question.type)));

      if (page >= 0) {
        setErrors({});
        setScreen({ kind: "answering", page, step: definition.pages.slice(0, page).reduce((count, each) => count + each.questions.length, 0) });
      }
    };
    window.addEventListener("message", received);

    return () => window.removeEventListener("message", received);
  }, [preview, definition]);

  // The Preview beside Look says where the survey is (W32): "Page 2 of 4", the welcome or the end.
  const at = screen.kind === "answering" ? screen.page + 1 : screen.kind === "welcome" ? "welcome" : screen.kind === "done" ? "end" : null;

  useEffect(() => {
    if (preview && definition !== null && at !== null && window.parent !== window) {
      window.parent.postMessage({ type: "askwerk:preview-page", at, pages: definition.pages.length }, window.location.origin);
    }
  }, [preview, definition, at]);

  useEffect(() => {
    document.documentElement.lang = uiLanguage;
  }, [uiLanguage]);

  // The error summary takes the focus, so screen readers read what is missing first (W8 B).
  useEffect(() => {
    if (Object.keys(errors).length > 0) {
      summary.current?.focus();
    }
  }, [errors]);

  if (screen.kind === "loading" || definition === null) {
    return (
      <Frame organisation={organisation} t={t}>
        {screen.kind === "not-found" ? (
          <Message title={t("notFoundTitle")} text={t("notFoundText")} />
        ) : screen.kind === "failed" ? (
          <Failure t={t} traceId={screen.traceId} onRetry={screen.retry} />
        ) : (
          <p role="status" aria-busy="true" className="run-muted">
            {t("loading")}
          </p>
        )}
      </Frame>
    );
  }

  const survey = definition;
  const surveyTitle = textOf(definition.welcome?.title, language, definition) || t("welcome");
  const oneAtATime = survey.layout === "one-at-a-time";
  // Logic (AWK-31): the respondent's way with the answers so far; hidden questions and skipped pages are left out.
  const way = route(survey, values);
  const pages = way.map((routed) => routed.page);
  const shownOn = (pageIndex: number) => way[pageIndex]?.questions ?? [];
  const steps = way.flatMap((routed, pageIndex) => routed.questions.map((question, index) => ({ pageIndex, index, question })));
  const answerable = steps.filter((step) => step.question.type !== "text-block" && step.question.type !== "image");
  const languageSwitch = survey.languages.length > 1 && (
    <LanguageMenu label={t("language")} languages={survey.languages} value={language} onChange={setLanguage} className="run-language" />
  );

  /** Only answered questions go to the server; empty values are left out. */
  const pageAnswers = (pageIndex: number): Answers =>
    Object.fromEntries(
      shownOn(pageIndex).flatMap((question) => {
        const value = values[question.id];
        return value === undefined || isEmpty(value) ? [] : [[question.id, clean(question, value)]];
      }),
    );

  const errorText = (question: Question, code: string) => messageFor(t, question, code);

  const showErrors = (problems: Record<string, readonly string[]>) => {
    setErrors(Object.fromEntries(Object.entries(problems).map(([questionId, codes]) => [questionId, codes[0] ?? "required"])));
  };

  /** Saves a page; on the last page also completes. Returns whether it went through. */
  async function savePage(pageIndex: number): Promise<boolean> {
    setBusy(true);

    try {
      if (!started.current) {
        await client.start(surveyId, language);
        started.current = true;
      }

      await client.savePage(surveyId, pages[pageIndex]!.id, pageAnswers(pageIndex));
      setErrors({});

      if (pageIndex === pages.length - 1) {
        await client.complete(surveyId);
        setScreen({ kind: "done" });
      }

      return true;
    } catch (error) {
      if (error instanceof ApiError && error.status === 400 && error.problem.errors) {
        const problems = error.problem.errors;
        const firstPage = way.findIndex((routed) => routed.questions.some((question) => problems[question.id] !== undefined));
        showErrors(problems);

        if (firstPage >= 0 && firstPage !== pageIndex) {
          setScreen({ kind: "answering", page: firstPage, step: steps.findIndex((step) => problems[step.question.id] !== undefined) });
        } else if (oneAtATime) {
          setScreen({ kind: "answering", page: pageIndex, step: steps.findIndex((step) => problems[step.question.id] !== undefined) });
        }
      } else if (!refused(error)) {
        failed(error, () => setScreen({ kind: "answering", page: pageIndex, step: steps.findIndex((step) => step.pageIndex === pageIndex) }));
      }

      return false;
    } finally {
      setBusy(false);
    }
  }

  /** The survey closed meanwhile, or this browser has answered already (W24): the matching page instead of an error. */
  function refused(error: unknown): boolean {
    const code = error instanceof ApiError && error.status === 409 ? error.problem.code : undefined;

    if (code === "closed") {
      setScreen({ kind: "closed" });
    } else if (code === "already-answered") {
      setScreen({ kind: "already" });
    }

    return code === "closed" || code === "already-answered";
  }

  async function start() {
    try {
      const response = await client.start(surveyId, language);
      started.current = true;
      const first = Math.max(0, pages.findIndex((page) => page.id === response.pageId));
      setScreen({ kind: "answering", page: first, step: steps.findIndex((step) => step.pageIndex === first) });
    } catch (error) {
      if (!refused(error)) {
        failed(error, () => setScreen({ kind: "welcome" }));
      }
    }
  }

  /** More than one response per browser (W24 C4): a new, empty response from the first page. */
  function answerAgain() {
    setValues({});
    setErrors({});
    void start();
  }

  const setValue = (questionId: string, value: AnswerValue | undefined) =>
    setValues((current) => {
      const next = { ...current };

      if (value === undefined) {
        delete next[questionId];
      } else {
        next[questionId] = value;
      }

      return next;
    });

  switch (screen.kind) {
    case "not-found":
      return (
        <Frame organisation={organisation} t={t}>
          <Message title={t("notFoundTitle")} text={t("notFoundText")} />
        </Frame>
      );
    case "closed":
      return (
        <Frame organisation={organisation} t={t} languageSwitch={languageSwitch}>
          <Message title={t("closedTitle")} text={t("closedText")} detail={screen.closedAt === undefined ? undefined : t("closedOn", { date: longDate(screen.closedAt, uiLanguage, timeZone) })} />
        </Frame>
      );
    case "already":
      return (
        <Frame organisation={organisation} t={t}>
          <Message title={t("alreadyTitle")} text={t("alreadyText")} />
        </Frame>
      );
    case "failed":
      return (
        <Frame organisation={organisation} t={t}>
          <Failure t={t} traceId={screen.traceId} onRetry={screen.retry} />
        </Frame>
      );
    case "done":
      return (
        <Frame organisation={organisation} t={t}>
          <div className="run-message">
            <h1>{textOf(survey.endPage?.text, language, survey) || t("endDefault")}</h1>
            <p className="run-muted">{t("endClose")}</p>
            {!oneResponsePerPerson && (
              <button type="button" className="run-button" onClick={answerAgain}>
                {t("answerAgain")}
              </button>
            )}
          </div>
        </Frame>
      );
    case "welcome": {
      const count = answerable.length;
      const minutes = Math.max(1, Math.round(count * 0.3));

      return (
        <Frame organisation={organisation} t={t} languageSwitch={languageSwitch}>
          <div
            className="run-welcome"
            onKeyDown={(event) => {
              if (event.key === "Enter" && event.target === event.currentTarget) {
                void start();
              }
            }}
          >
            <h1>{textOf(survey.welcome?.title, language, survey) || t("welcome")}</h1>
            {textOf(survey.welcome?.text, language, survey) && <p className="run-lead">{textOf(survey.welcome?.text, language, survey)}</p>}
            <p className="run-meta">{count === 1 ? t("durationSingle") : minutes === 1 ? t("durationOne", { count }) : t("duration", { minutes, count })}</p>
            <div>
              <button type="button" className="run-button run-button--primary run-button--wide" onClick={() => void start()}>
                {t("start")}
              </button>
            </div>
          </div>
        </Frame>
      );
    }
    case "welcome-back":
      return (
        <Frame organisation={organisation} t={t} languageSwitch={languageSwitch}>
          <div className="run-message">
            <h1>{t("welcomeBackTitle")}</h1>
            <p className="run-lead">{t("welcomeBackText")}</p>
            <div>
              <button
                type="button"
                className="run-button run-button--primary run-button--wide"
                onClick={() => {
                  const page = Math.max(0, pages.findIndex((each) => each.id === screen.response.pageId));
                  setScreen({ kind: "answering", page, step: steps.findIndex((step) => step.pageIndex === page) });
                }}
              >
                {t("continue")}
              </button>
            </div>
          </div>
        </Frame>
      );
    case "answering":
      break;
  }

  const errorSummary = Object.keys(errors).length > 0 && (
    <div ref={summary} className="run-summary" role="alert" tabIndex={-1}>
      <b>{Object.keys(errors).length === 1 ? t("missingOne") : t("missing", { count: Object.keys(errors).length })}</b>
      {steps
        .filter((step) => errors[step.question.id] !== undefined)
        .map((step) => (
          <a
            key={step.question.id}
            href={`#q-${step.question.id}`}
            onClick={(event) => {
              event.preventDefault();
              document.getElementById(`q-${step.question.id}`)?.focus();
            }}
          >
            {textOf(step.question.text, language, survey)}
          </a>
        ))}
    </div>
  );

  if (!oneAtATime) {
    // A changed answer can shorten the way; the respondent then stays on its last page.
    const pageIndex = Math.min(screen.page, pages.length - 1);
    const page = pages[pageIndex]!;
    const last = pageIndex === pages.length - 1;
    const pageTitle = textOf(page.title, language, survey);

    return (
      <Frame organisation={organisation} t={t} languageSwitch={languageSwitch} overTitle={{ title: surveyTitle, sub: `${t("pageOf", { page: pageIndex + 1, pages: pages.length })}${pageTitle ? ` · ${pageTitle}` : ""}` }}>
        <form
          className="run-page"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            void savePage(pageIndex).then((saved) => {
              if (saved && !last) {
                setScreen({ kind: "answering", page: pageIndex + 1, step: 0 });
                window.scrollTo?.(0, 0);
              }
            });
          }}
        >
          <h1 className="run-visually-hidden">{surveyTitle}</h1>
          <Progress label={`${t("pageOf", { page: pageIndex + 1, pages: pages.length })}${pageTitle ? ` · ${pageTitle}` : ""}`} share={(pageIndex + 1) / (pages.length + 1)} step={pageIndex + 1} steps={pages.length} />
          {errorSummary}
          {shownOn(pageIndex).map((question) => (
            <QuestionField
              key={question.id}
              t={t}
              definition={survey}
              language={language}
              question={question}
              value={values[question.id]}
              error={errors[question.id] ? errorText(question, errors[question.id]!) : undefined}
              onChange={(value) => setValue(question.id, value)}
            />
          ))}
          <div className="run-nav">
            {pageIndex > 0 ? (
              <button type="button" className="run-button" onClick={() => setScreen({ kind: "answering", page: pageIndex - 1, step: 0 })}>
                {t("back")}
              </button>
            ) : (
              <span />
            )}
            <span className="run-saved">{t("saved")}</span>
            <button type="submit" className="run-button run-button--primary" disabled={busy}>
              {last ? t("send") : t("next")}
            </button>
          </div>
        </form>
      </Frame>
    );
  }

  // One at a time: a step per question; the page is saved when its last question is done.
  const stepIndex = Math.max(0, screen.step);
  const current = steps[stepIndex]!;
  const answeredNumber = answerable.findIndex((step) => step.question.id === current.question.id) + 1;

  async function next(from = stepIndex) {
    const step = steps[from]!;
    const lastOfPage = from === steps.length - 1 || steps[from + 1]!.pageIndex !== step.pageIndex;

    if (lastOfPage) {
      const saved = await savePage(step.pageIndex);

      if (!saved || step.pageIndex === pages.length - 1) {
        return;
      }
    }

    setScreen({ kind: "answering", page: steps[from + 1]!.pageIndex, step: from + 1 });
  }

  const previous = () => stepIndex > 0 && setScreen({ kind: "answering", page: steps[stepIndex - 1]!.pageIndex, step: stepIndex - 1 });

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== "Enter" || (event.target as HTMLElement).tagName === "TEXTAREA" && !event.ctrlKey) {
      return;
    }

    event.preventDefault();

    if (event.shiftKey) {
      previous();
    } else {
      void next();
    }
  };

  return (
    <Frame organisation={organisation} t={t} languageSwitch={languageSwitch} wide overTitle={{ title: surveyTitle, sub: t("questionOf", { number: Math.max(1, answeredNumber), total: answerable.length }) }}>
      <div className="run-page run-page--one">
        <h1 className="run-visually-hidden">{surveyTitle}</h1>
        <Progress label={t("questionOf", { number: Math.max(1, answeredNumber), total: answerable.length })} share={stepIndex / steps.length} step={Math.max(1, answeredNumber)} steps={answerable.length} />
        {errorSummary}
        <QuestionField
          key={current.question.id}
          t={t}
          definition={survey}
          language={language}
          question={current.question}
          value={values[current.question.id]}
          error={errors[current.question.id] ? errorText(current.question, errors[current.question.id]!) : undefined}
          onChange={(value) => setValue(current.question.id, value)}
          onKeyDown={onKeyDown}
          big
        />
        <div className="run-nav run-nav--one">
          <button type="button" className="run-button run-button--primary" disabled={busy} onClick={() => void next()}>
            {t("ok")} <kbd aria-hidden="true">↵</kbd>
          </button>
        </div>
        <div className="run-keys">
          <button type="button" className="run-link" disabled={stepIndex === 0} onClick={previous}>
            ↑ {t("previous")}
          </button>
          <span aria-hidden="true">
            <kbd>Shift</kbd>+<kbd>↵</kbd> {t("keysBack")} · <kbd>↵</kbd> {t("keysNext")}
          </span>
        </div>
      </div>
    </Frame>
  );
}

function isEmpty(value: AnswerValue): boolean {
  return (
    value.option === undefined &&
    (value.options?.length ?? 0) === 0 &&
    value.number === undefined &&
    (value.text ?? "").trim() === "" &&
    (value.date ?? "") === "" &&
    Object.keys(value.rows ?? {}).length === 0 &&
    (value.order?.length ?? 0) === 0 &&
    value.dontKnow !== true
  );
}

/** Keeps only what the type uses; the "Other" text only while "Other" is chosen. */
function clean(question: Question, value: AnswerValue): AnswerValue {
  if (value.dontKnow) {
    return { dontKnow: true };
  }

  const otherChosen = question.other !== undefined && (value.option === question.other.code || value.options?.includes(question.other.code) === true);
  const { other, ...rest } = value;

  return otherChosen ? { ...rest, other: other ?? "" } : rest;
}

function messageFor(t: ReturnType<typeof createTranslator<keyof typeof runnerMessages.en>>, question: Question, code: string): string {
  const min = question.type === "nps" ? 0 : (question.scale?.min ?? (question.type === "slider" ? 0 : 1));
  const max = question.type === "nps" ? 10 : (question.scale?.max ?? (question.type === "slider" ? 100 : 5));

  switch (code) {
    case "required":
      if (["rating", "nps", "slider"].includes(question.type)) {
        return t("errorChooseValue", { min, max });
      }

      if (question.type === "yes-no") {
        return t("errorChooseYesNo");
      }

      return ["single", "multiple", "dropdown", "ranking"].includes(question.type) ? t("errorChoose") : question.type === "matrix" ? t("errorRows") : t("errorEnter");
    case "number-out-of-range":
      return t("errorChooseValue", { min, max });
    case "number-not-whole":
      return t("errorWhole");
    case "number-off-step":
      return t("errorStep", { step: question.scale?.step ?? 1 });
    case "other-text-missing":
      return t("errorOther");
    case "format-email":
      return t("errorEmail");
    case "format-phone":
      return t("errorPhone");
    case "format-url":
      return t("errorUrl");
    case "date-invalid":
      return t("errorDate");
    case "date-too-early":
      return t("errorEarly");
    case "date-too-late":
      return t("errorLate");
    case "rows-missing":
      return t("errorRows");
    case "order-invalid":
      return t("errorOrder");
    case "text-too-long":
      return t("errorLong");
    default:
      return t("errorCheck");
  }
}

function Frame({
  organisation,
  t,
  languageSwitch,
  wide = false,
  overTitle,
  children,
}: {
  organisation: string;
  t: (key: "language" | "previewBanner") => string;
  languageSwitch?: ReactNode;
  wide?: boolean;
  /** Question pages: the survey's title and where the respondent is, shown over the picture when the look says so (W28). */
  overTitle?: { title: string; sub: string };
  children: ReactNode;
}) {
  const preview = useContext(PreviewMode);
  const look = useContext(LookContext);
  const prefersDark = usePrefersDark();
  const fromAddress = useContext(HostStyle);
  const saved = useContext(SavedHostLook);
  const host = fromAddress === null ? null : hostStyleOf({ ...saved, ...fromAddress });
  const dark = host === null && look?.dark !== undefined && (look.dark.always || prefersDark);

  return (
    <div className={`run${wide ? " run--one" : ""}`} style={{ ...(dark ? { ...look.style, ...look.dark!.style } : look?.style), ...host }} {...look?.data} data-mode={dark ? "dark" : undefined}>
      <header className="run-top">
        {preview && (
          <p role="note" className="run-preview">
            {t("previewBanner")}
          </p>
        )}
        <div className="run-head">
          <span className="run-org">
            {look?.logo !== undefined ? (
              <img className="run-logo" src={look.logo} alt={organisation} />
            ) : (
              <>
                {organisation && <i aria-hidden="true">{organisation.slice(0, 1).toUpperCase()}</i>}
                {organisation}
              </>
            )}
          </span>
          {languageSwitch}
        </div>
      </header>
      {look?.hero !== undefined && (
        <div className="run-hero">
          <img src={look.hero.src} srcSet={look.hero.srcSet} sizes="100vw" alt={heroAlt(look.hero.alt)} style={{ objectPosition: look.hero.position }} />
        </div>
      )}
      {look?.hero === undefined && look !== null && look.pattern !== "none" && look.pattern !== "mountains" && <div className={`run-pattern run-pattern--${look.pattern}`} aria-hidden="true" />}
      {overTitle !== undefined && look?.data["data-title"] === "over-picture" && (
        // The same words are in the page for screen readers; this is the picture's caption.
        <div className="run-overtitle" aria-hidden="true">
          <p className="run-overtitle__title">{overTitle.title}</p>
          <p className="run-overtitle__sub">{overTitle.sub}</p>
        </div>
      )}
      <main className="run-main">{children}</main>
      <span hidden>{t("language")}</span>
    </div>
  );
}

/** The progress (W26): the ruler, a bar, real steps (one per page or question), or the label only. */
function Progress({ label, share, step, steps }: { label: string; share: number; step: number; steps: number }) {
  const look = useContext(LookContext);

  return (
    <div className="run-progress">
      <span className="run-progress__label" role="status">
        {label}
      </span>
      {look?.progress === "steps" && steps <= 24 ? (
        <div className="run-steps" aria-hidden="true">
          {Array.from({ length: steps }, (_, index) => (
            <i key={index} className={index < step ? "run-steps__on" : undefined} />
          ))}
        </div>
      ) : (
        look?.progress !== "none" && (
          <div className="run-ruler" aria-hidden="true">
            <i style={{ width: `${Math.round(Math.min(1, share) * 100)}%` }} />
          </div>
        )
      )}
    </div>
  );
}

function Message({ title, text, detail }: { title: string; text: string; detail?: string | undefined }) {
  return (
    <div className="run-message">
      <h1>{title}</h1>
      {detail !== undefined && <p className="run-lead">{detail}</p>}
      <p className="run-lead">{text}</p>
    </div>
  );
}

/** A day in words in the respondent's language, as it was in the organisation's time zone (W24): "1. November 2026". */
function longDate(at: string, language: "de" | "en", timeZone: string | undefined): string {
  return new Intl.DateTimeFormat(language === "de" ? "de" : "en-GB", { dateStyle: "long", timeZone }).format(new Date(at));
}

function Failure({ t, traceId, onRetry }: { t: ReturnType<typeof createTranslator<keyof typeof runnerMessages.en>>; traceId: string | undefined; onRetry: () => void }) {
  return (
    <div className="run-message">
      <h1>{t("errorTitle")}</h1>
      <p className="run-lead">{t("errorText")}</p>
      {traceId && <p className="run-muted run-mono">{t("reference", { traceId })}</p>}
      <div>
        <button type="button" className="run-button run-button--primary" onClick={onRetry}>
          {t("tryAgain")}
        </button>
      </div>
    </div>
  );
}

/** The alt text of the survey's own header image in the page's language; a theme's header image is decorative. */
function heroAlt(alt: TranslatedText | undefined): string {
  if (alt === undefined) {
    return "";
  }

  const language = document.documentElement.lang;

  return alt[language] ?? Object.values(alt)[0] ?? "";
}

/** The question and value of an answer from the email, when it fits: a number on the scale, or one of the codes. */
function answerFromEmail(survey: SurveyDefinition, answer: { questionId: string; value: string }): { question: Question; value: AnswerValue } | undefined {
  const question = survey.pages.flatMap((page) => page.questions).find((each) => each.id === answer.questionId);

  if (question === undefined) {
    return undefined;
  }

  const number = Number(answer.value);

  switch (question.type) {
    case "rating":
      return Number.isInteger(number) && number >= (question.scale?.min ?? 1) && number <= (question.scale?.max ?? 5) ? { question, value: { number } } : undefined;
    case "nps":
      return Number.isInteger(number) && number >= 0 && number <= 10 ? { question, value: { number } } : undefined;
    case "yes-no":
      return [question.yes?.code ?? "1", question.no?.code ?? "0"].includes(answer.value) ? { question, value: { option: answer.value } } : undefined;
    case "single":
      return question.options?.some((option) => option.code === answer.value) ? { question, value: { option: answer.value } } : undefined;
    default:
      return undefined;
  }
}

/** The saved host look as the runner takes it: only the values that are set. */
function hostLookOf(saved: { accent?: string | null; background?: string | null; font?: string | null; radius?: number | null } | null | undefined): HostLook | null {
  if (saved == null) {
    return null;
  }

  const look: HostLook = {};
  saved.accent != null && (look.accent = saved.accent);
  saved.background != null && (look.background = saved.background);
  saved.font != null && (look.font = saved.font);
  saved.radius != null && (look.radius = saved.radius);

  return look;
}

/** The style of the host look on the runner's page: its variables, and its background instead of the survey's. */
function hostStyleOf(look: HostLook): Record<string, string> {
  return { ...hostLookVariables(look, "run"), ...(look.background === undefined ? {} : { background: look.background, backgroundImage: "none" }) };
}
