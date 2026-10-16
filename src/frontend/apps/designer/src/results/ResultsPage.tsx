// SPDX-License-Identifier: AGPL-3.0-only
import "../designer/designer.css";
import "./results.css";
import type { ExportItem, QuestionResult, ResponseDetail, ResponsePage, ResponseRow, Results, ResultsClient, ResultsFilter } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Skeleton, DataGrid, dataGridTexts, type DataGridColumn, Tabs as SharedTabs } from "@askwerk/ui";
import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { SurveyFrame, type SurveyTab, type WorkspaceSurveys } from "../workspace";
import { ExportDialog, fileSize } from "./ExportDialog";
import { resultsMessages, type ResultsMessageKey } from "./messages";

type T = (key: ResultsMessageKey, values?: Record<string, string | number>) => string;

type ResultsPageProps = {
  client: ResultsClient;
  /** The survey's bar and tabs (AWK-29, W17). */
  surveys: WorkspaceSurveys;
  surveyId: string;
  /** The survey's address, to share while there are no responses. */
  surveyLink: string;
  locale: Locale;
  /** Goes to another part of the survey. */
  onTab: (tab: SurveyTab) => void;
  onBack: () => void;
  onOpenSurvey?: (surveyId: string) => void;
  /** The profile menu at the bar's far right (W23). */
  profile?: ReactNode;
  /** Pause after typing before searching; tests use 0. */
  searchDelayMs?: number;
  /** How often a running export is asked about; tests use 0. */
  pollMs?: number;
  /** Editors get every tab; analysts and viewers Results and Participants (AWK-27, W17). */
  canEdit?: boolean;
  /** Opens a new tab, for Preview in a new tab and its PDF. */
  openWindow?: (url: string) => void;
  timeZone?: string;
  now?: () => Date;
};

type Status = "" | "complete" | "partial";
type Time = "all" | "today" | "7" | "30";
type Tab = "overview" | "responses";
type Loaded = { kind: "loading" } | { kind: "failed" } | { kind: "ready"; results: Results };

const choiceTypes = new Set(["single", "multiple", "dropdown", "yes-no"]);

/** Start of the chosen time range (UTC, ISO); none for all time. */
function fromOf(time: Time, now: Date): string | undefined {
  if (time === "all") {
    return undefined;
  }

  const start = new Date(now);
  start.setHours(0, 0, 0, 0);

  if (time !== "today") {
    start.setDate(start.getDate() - Number(time) + 1);
  }

  return start.toISOString();
}

/**
 * Results of a survey (AWK-24, W11), stupid simple: totals, filters as preset chips, and two tabs. Overview shows a
 * card per question; Responses lists them newest first, loading more at the end, one response in a side panel.
 */
export function ResultsPage({ client, surveys, surveyId, surveyLink, locale, onTab, onBack, onOpenSurvey, profile, searchDelayMs = 250, pollMs = 1500, canEdit = true, openWindow = (url) => void window.open(url, "_blank", "noopener"), timeZone, now = () => new Date() }: ResultsPageProps) {
  const t: T = useMemo(() => createTranslator(resultsMessages, locale), [locale]);
  const [loaded, setLoaded] = useState<Loaded>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);
  // Labels start in the user's language; the server falls back to the survey's default when it does not offer it.
  const [language, setLanguage] = useState<string>(locale);
  const [tab, setTab] = useState<Tab>("overview");
  const [overviewStatus, setOverviewStatus] = useState<Status>("complete");
  const [responsesStatus, setResponsesStatus] = useState<Status>("");
  const [time, setTime] = useState<Time>("all");
  const [version, setVersion] = useState(0);
  const [exporting, setExporting] = useState(false);
  // An export still being prepared after its dialog closed, and the notice once it is ready (W14 B).
  const [waiting, setWaiting] = useState<ExportItem | null>(null);
  const [readyExport, setReadyExport] = useState<ExportItem | null>(null);

  useEffect(() => {
    if (waiting === null) {
      return;
    }

    let current = true;
    const timer = setTimeout(() => {
      client.exportOf(waiting.id).then(
        (item) => {
          if (!current) {
            return;
          }

          setWaiting(item.status === "queued" || item.status === "running" ? item : null);

          if (item.status === "ready") {
            setReadyExport(item);
          }
        },
        () => undefined,
      );
    }, pollMs);

    return () => {
      current = false;
      clearTimeout(timer);
    };
  }, [client, waiting, pollMs]);
  // The range starts when it is chosen, not on every render.
  const from = useMemo(() => fromOf(time, now()), [time]);
  const base: ResultsFilter = useMemo(() => ({ ...(from === undefined ? {} : { from }), ...(version > 0 ? { version } : {}) }), [from, version]);
  const status = tab === "overview" ? overviewStatus : responsesStatus;
  const setStatus = tab === "overview" ? setOverviewStatus : setResponsesStatus;

  useEffect(() => {
    let current = true;
    setLoaded((previous) => (previous.kind === "ready" ? previous : { kind: "loading" }));
    client.overview(surveyId, { ...base, ...(overviewStatus === "" ? {} : { status: overviewStatus }) }, language).then(
      (results) => current && setLoaded({ kind: "ready", results }),
      () => current && setLoaded({ kind: "failed" }),
    );

    return () => {
      current = false;
    };
  }, [client, surveyId, base, overviewStatus, language, attempt]);

  const results = loaded.kind === "ready" ? loaded.results : null;
  const shownLanguage = results === null || results.languages.includes(language) ? language : results.defaultLanguage;

  return (
    <SurveyFrame
      surveys={surveys}
      surveyId={surveyId}
      locale={locale}
      canEdit={canEdit}
      onTab={onTab}
      onBack={onBack}
      {...(onOpenSurvey === undefined ? {} : { onOpenSurvey })}
      openWindow={openWindow}
      profile={profile}
      active="results"
      className="results"
      {...(results !== null && results.languages.length > 1 ? { languages: results.languages, language: shownLanguage, onLanguage: setLanguage } : { language: shownLanguage })}
      actions={
        results !== null &&
        results.published &&
        results.totals.responses > 0 && (
          <button type="button" className="designer-button designer-button--quiet" onClick={() => setExporting(true)}>
            {t("export")}
          </button>
        )
      }
    >
      {() => (
      <>
      {exporting && results !== null && (
        <ExportDialog
          t={t}
          client={client}
          surveyId={surveyId}
          filter={{ ...base, ...(status === "" ? {} : { status }) }}
          language={shownLanguage}
          count={status === "complete" ? results.totals.complete : status === "partial" ? results.totals.partial : results.totals.responses}
          locale={locale === "de" ? "de" : "en"}
          timeZone={timeZone}
          pollMs={pollMs}
          onClose={(stillPreparing) => {
            setExporting(false);
            setWaiting(stillPreparing);
          }}
        />
      )}
      {readyExport !== null && readyExport.file !== null && (
        <div role="status" aria-label={t("exportReadyNotice")} className="results-notice">
          <span>
            <b>{t("exportReadyNotice")}</b>
            <span className="results-card__muted">
              {readyExport.file.name} · {fileSize(readyExport.file.size)}
            </span>
          </span>
          <a href={client.exportFileUrl(readyExport.id)} download={readyExport.file.name} onClick={() => setReadyExport(null)}>
            {t("download")}
          </a>
          <button type="button" className="results-notice__close" aria-label={t("dismiss")} onClick={() => setReadyExport(null)}>
            ✕
          </button>
        </div>
      )}
      <main className="results-main">
        {loaded.kind === "loading" && (
          <div role="status" aria-busy="true" className="results-loading">
            <span className="results-hidden">{t("loading")}</span>
            <Skeleton width="220px" height="28px" />
            <Skeleton width="420px" height="16px" />
            <Skeleton width="100%" height="160px" />
            <Skeleton width="100%" height="160px" />
          </div>
        )}
        {loaded.kind === "failed" && (
          <State title={t("failed")} hint={t("failedHint")}>
            <button type="button" className="designer-button" onClick={() => setAttempt((count) => count + 1)}>
              {t("tryAgain")}
            </button>
          </State>
        )}
        {results !== null && !results.published && (
          <State title={t("notPublished")} hint={t("notPublishedHint")}>
            {canEdit ? (
              <button type="button" className="designer-button" onClick={() => onTab("build")}>
                {t("backToEdit")}
              </button>
            ) : (
              <p className="results-card__muted">{t("previewUntilThen")}</p>
            )}
          </State>
        )}
        {results !== null && results.published && (
          <>
            <div className="results-head">
              <h2 className="results-total">
                {results.totals.responses === 1 ? t("responsesOne") : t("responsesMany", { count: results.totals.responses })}
              </h2>
              <p className="results-meta">
                {[
                  t("totals", { complete: results.totals.complete, partial: results.totals.partial }),
                  ...(results.totals.medianSeconds === null ? [] : [t("median", { minutes: Math.max(1, Math.round(results.totals.medianSeconds / 60)) })]),
                ].join(" · ")}
              </p>
              <div className="results-filters" role="group" aria-label={t("filters")}>
                <Chip
                  label={t("status")}
                  value={status}
                  onChange={(value) => setStatus(value as Status)}
                  options={[
                    { value: "", label: t("statusAll") },
                    { value: "complete", label: t("statusComplete") },
                    { value: "partial", label: t("statusPartial") },
                  ]}
                />
                <Chip
                  label={t("time")}
                  value={time}
                  onChange={(value) => setTime(value as Time)}
                  options={[
                    { value: "all", label: t("timeAll") },
                    { value: "today", label: t("timeToday") },
                    { value: "7", label: t("time7") },
                    { value: "30", label: t("time30") },
                  ]}
                />
                {results.versions.length > 1 && (
                  <Chip
                    label={t("version")}
                    value={String(version)}
                    onChange={(value) => setVersion(Number(value))}
                    options={[{ value: "0", label: t("versionAll") }, ...[...results.versions].reverse().map((number) => ({ value: String(number), label: t("versionN", { number }) }))]}
                  />
                )}
              </div>
              <Tabs t={t} tab={tab} onTab={setTab} surveyId={surveyId} tabs={["overview", "responses"]} />
            </div>
            {results.totals.responses === 0 ? (
              <State title={t("noResponses")} hint={t("noResponsesHint")}>
                <p className="results-link">{surveyLink}</p>
                <CopyButton t={t} text={surveyLink} />
              </State>
            ) : tab === "overview" ? (
              <div id={`${surveyId}-panel-overview`} role="tabpanel" aria-labelledby={`${surveyId}-tab-overview`} className="results-overview">
                <nav className="results-outline" aria-label={t("questions")}>
                  <ol>
                    {results.questions.map((question, index) => (
                      <li key={question.id}>
                        <a href={`#result-${question.id}`}>
                          {index + 1} {question.variable}
                        </a>
                      </li>
                    ))}
                  </ol>
                </nav>
                <div className="results-cards">
                  {results.questions.map((question, index) => (
                    <QuestionCard key={question.id} t={t} locale={locale} question={question} number={index + 1} timeZone={timeZone} onShowAll={() => setTab("responses")} />
                  ))}
                </div>
              </div>
            ) : (
              <div id={`${surveyId}-panel-responses`} role="tabpanel" aria-labelledby={`${surveyId}-tab-responses`}>
                <ResponseList
                  t={t}
                  locale={locale}
                  client={client}
                  surveyId={surveyId}
                  filter={{ ...base, ...(responsesStatus === "" ? {} : { status: responsesStatus }) }}
                  language={shownLanguage}
                  searchDelayMs={searchDelayMs}
                  timeZone={timeZone}
                />
              </div>
            )}
          </>
        )}
      </main>
      </>
      )}
    </SurveyFrame>
  );
}

function State({ title, hint, children }: { title: string; hint: string; children: ReactNode }) {
  return (
    <div className="results-state">
      <p className="results-state__title">{title}</p>
      <p>{hint}</p>
      {children}
    </div>
  );
}

function CopyButton({ t, text }: { t: T; text: string }) {
  const [copied, setCopied] = useState(false);

  return (
    <button
      type="button"
      className="designer-button results-primary"
      onClick={() => {
        void navigator.clipboard?.writeText(text).then(() => setCopied(true));
      }}
    >
      {copied ? t("copied") : t("copyLink")}
    </button>
  );
}

function Chip({ label, value, options, onChange }: { label: string; value: string; options: { value: string; label: string }[]; onChange: (value: string) => void }) {
  const id = useId();

  return (
    <span className="results-chip">
      <span id={id} className="results-hidden">
        {label}
      </span>
      <ComboBox labelledBy={id} options={options} value={value} onChange={onChange} texts={{ choose: label }} />
    </span>
  );
}

function Tabs({ t, tab, onTab, surveyId, tabs }: { t: T; tab: Tab; onTab: (tab: Tab) => void; surveyId: string; tabs: readonly Tab[] }) {
  return <SharedTabs label={t("tabs")} tabs={tabs.map((each) => [each, t(each)] as const)} value={tab} onChange={onTab} idPrefix={surveyId} />;
}

const percent = (count: number, of: number) => (of === 0 ? 0 : Math.round((count * 100) / of));

function QuestionCard({ t, locale, question, number, timeZone, onShowAll }: { t: T; locale: Locale; question: QuestionResult; number: number; timeZone: string | undefined; onShowAll: () => void }) {
  const name = `${number} ${question.text}`;
  const decimal = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
  const max = Math.max(1, ...(question.distribution ?? []).map((value) => value.count));
  const total = (question.distribution ?? []).reduce((sum, value) => sum + value.count, 0);

  return (
    <section id={`result-${question.id}`} className="results-card" aria-label={name}>
      <h3>
        <span className="results-card__number">{number}</span> {question.text}
      </h3>
      <p className="results-card__meta">
        {[question.variable, question.answered === 0 ? t("noAnswers") : t("answered", { count: question.answered }), ...(question.dontKnow > 0 ? [t("dontKnow", { count: question.dontKnow })] : [])].join(" · ")}
      </p>
      {question.options !== null && choiceTypes.has(question.type) && (
        <ul className="results-bars">
          {question.options.map((option) => (
            <li key={option.code}>
              <span className="results-bars__label">{option.label}</span>
              <span className="results-bars__track" aria-hidden="true">
                <span className="results-bars__fill" style={{ width: `${percent(option.count, question.answered)}%` }} />
              </span>
              <span className="results-bars__value">
                {option.count} · {percent(option.count, question.answered)} %
              </span>
            </li>
          ))}
        </ul>
      )}
      {question.options !== null && question.type === "ranking" && (
        <ol className="results-ranking">
          {[...question.options]
            .sort((a, b) => (a.averagePosition ?? Infinity) - (b.averagePosition ?? Infinity))
            .map((option) => (
              <li key={option.code}>
                {option.label}
                {option.averagePosition !== null && <span className="results-card__muted"> · {t("averagePlace", { place: decimal.format(option.averagePosition) })}</span>}
              </li>
            ))}
        </ol>
      )}
      {question.nps !== null ? (
        <div className="results-figure">
          <p className="results-big">{t("nps", { score: question.nps.score })}</p>
          <p className="results-card__muted">{t("npsGroups", question.nps)}</p>
        </div>
      ) : (
        question.numbers !== null && (
          <div className="results-figure">
            <p className="results-big">{decimal.format(question.numbers.average)}</p>
            <p className="results-card__muted">
              {question.type === "number"
                ? `${t("averageMedian", { median: decimal.format(question.numbers.median) })} · ${t("numberRange", { min: decimal.format(question.numbers.min), max: decimal.format(question.numbers.max) })}`
                : t("averageMedian", { median: decimal.format(question.numbers.median) })}
            </p>
          </div>
        )
      )}
      {question.distribution !== null && question.distribution.length > 0 && (
        <div className="results-columns" role="img" aria-label={`${t("spread")}: ${question.distribution.map((value) => `${decimal.format(value.value)}: ${value.count}`).join(", ")}`}>
          {question.distribution.map((value) => (
            <span key={value.value} className="results-columns__item">
              <span className="results-columns__share">{percent(value.count, total)} %</span>
              <span className="results-columns__bar" style={{ height: `${Math.round((value.count * 100) / max)}%` }} />
              <span className="results-columns__value">{decimal.format(value.value)}</span>
            </span>
          ))}
        </div>
      )}
      {question.rows !== null && (
        <div className="results-matrix">
          {question.rows.map((row) => {
            const answered = row.options.reduce((sum, option) => sum + option.count, 0);

            return (
              <div key={row.code} className="results-matrix__row">
                <span className="results-bars__label">{row.label}</span>
                <span className="results-matrix__stack" aria-hidden="true">
                  {row.options.map((option, index) => (
                    <span key={option.code} className={`results-matrix__part results-matrix__part--${index % 6}`} style={{ width: `${percent(option.count, answered)}%` }} />
                  ))}
                </span>
                <span className="results-bars__value">{row.options.map((option) => `${option.label} ${option.count}`).join(" · ")}</span>
              </div>
            );
          })}
        </div>
      )}
      {question.latest !== null && (
        <>
          <ul className="results-texts">
            {question.latest.map((text) => (
              <li key={`${text.at}-${text.text}`}>
                <span>{text.text}</span>
                <time dateTime={text.at}>{new Intl.DateTimeFormat(locale, { dateStyle: "medium", ...(timeZone === undefined ? {} : { timeZone }) }).format(new Date(text.at))}</time>
              </li>
            ))}
          </ul>
          {question.answered > question.latest.length && (
            <button type="button" className="results-link-button" onClick={onShowAll}>
              {t("showAll", { count: question.answered })}
            </button>
          )}
        </>
      )}
      {question.dates !== null && <p className="results-card__muted">{t("dates", question.dates)}</p>}
    </section>
  );
}

type ListState = { rows: ResponseRow[]; page: ResponsePage | null; loading: boolean; failed: boolean };

function ResponseList({
  t,
  locale,
  client,
  surveyId,
  filter,
  language,
  searchDelayMs,
  timeZone,
}: {
  t: T;
  locale: Locale;
  client: ResultsClient;
  surveyId: string;
  filter: ResultsFilter;
  language: string;
  searchDelayMs: number;
  timeZone: string | undefined;
}) {
  const [typed, setTyped] = useState("");
  const [search, setSearch] = useState("");
  const [list, setList] = useState<ListState>({ rows: [], page: null, loading: true, failed: false });
  const [open, setOpen] = useState<string | null>(null);
  // Responses sort by their start only, newest or oldest first, on the server's cursor (W13 B).
  const [order, setOrder] = useState<"newest" | "oldest">("newest");
  const [attempt, setAttempt] = useState(0);
  // One key for what the list shows; a new one starts it again from the top.
  const key = JSON.stringify([filter, language, search, order, attempt]);
  const query = { ...filter, ...(search === "" ? {} : { search }), ...(order === "oldest" ? { order } : {}) };
  const searchId = useId();

  useEffect(() => {
    const timer = setTimeout(() => setSearch(typed.trim()), searchDelayMs);

    return () => clearTimeout(timer);
  }, [typed, searchDelayMs]);

  useEffect(() => {
    let current = true;
    setList({ rows: [], page: null, loading: true, failed: false });
    client.responses(surveyId, query, language).then(
      (page) => current && setList({ rows: [...page.items], page, loading: false, failed: false }),
      () => current && setList({ rows: [], page: null, loading: false, failed: true }),
    );

    return () => {
      current = false;
    };
  }, [key]);

  const more = () => {
    const cursor = list.page?.next;

    if (cursor === null || cursor === undefined || list.loading) {
      return;
    }

    setList((current) => ({ ...current, loading: true }));
    client.responses(surveyId, { ...query, cursor }, language).then(
      (page) => setList((current) => ({ rows: [...current.rows, ...page.items], page, loading: false, failed: false })),
      () => setList((current) => ({ ...current, loading: false })),
    );
  };

  const dateTime = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short", ...(timeZone === undefined ? {} : { timeZone }) });
  const duration = (seconds: number | null) => (seconds === null ? "—" : seconds < 60 ? t("seconds", { count: seconds }) : t("minutes", { count: Math.round(seconds / 60) }));
  const columns = list.page?.columns ?? [];
  const index = open === null ? -1 : list.rows.findIndex((row) => row.id === open);
  const statusText = (status: ResponseRow["status"]) => (status === "complete" ? t("statusComplete") : t("statusPartial"));
  const gridColumns: DataGridColumn<ResponseRow>[] = [
    { key: "started", header: t("started"), align: "end", sortable: true, phone: "title", cell: (row) => dateTime.format(new Date(row.startedAt)) },
    { key: "status", header: t("status"), cell: (row) => statusText(row.status) },
    { key: "duration", header: t("duration"), align: "end", cell: (row) => duration(row.durationSeconds) },
    { key: "language", header: t("lang"), cell: (row) => row.language.toUpperCase() },
    // On phones only the first three answers show; the card opens the whole response (W13 D).
    ...columns.map((column, position): DataGridColumn<ResponseRow> => ({
      key: column.id,
      header: column.variable,
      ...(position >= 3 ? { phone: "hidden" as const } : {}),
      cell: (row) => row.answers[column.variable] ?? "",
    })),
  ];

  return (
    <div className={open === null ? "results-responses" : "results-responses results-responses--open"}>
      <div className="results-responses__list">
        <p className="results-search">
          <label htmlFor={searchId} className="results-hidden">
            {t("search")}
          </label>
          <input id={searchId} type="search" placeholder={`${t("search")}…`} value={typed} onChange={(event) => setTyped(event.target.value)} />
        </p>
        <DataGrid
          label={t("responses")}
          columns={gridColumns}
          rows={list.page === null ? null : list.rows}
          rowKey={(row) => row.id}
          sort={{ key: "started", descending: order === "newest" }}
          onSort={(next) => setOrder(next.descending ? "newest" : "oldest")}
          onOpen={(row) => setOpen(row.id)}
          active={open}
          stickyFirst
          more={{ total: list.page?.total ?? 0, loading: list.loading && list.page !== null, onMore: more }}
          empty={<p>{t("noMatch")}</p>}
          error={list.failed}
          onRetry={() => setAttempt((count) => count + 1)}
          texts={dataGridTexts(locale)}
        />
      </div>
      {open !== null && (
        <ResponsePanel
          t={t}
          client={client}
          surveyId={surveyId}
          responseId={open}
          statusText={statusText}
          dateTime={dateTime}
          duration={duration}
          onPrevious={index > 0 ? () => setOpen(list.rows[index - 1]!.id) : undefined}
          onNext={index >= 0 && index < list.rows.length - 1 ? () => setOpen(list.rows[index + 1]!.id) : undefined}
          onClose={() => setOpen(null)}
        />
      )}
    </div>
  );
}

function ResponsePanel({
  t,
  client,
  surveyId,
  responseId,
  statusText,
  dateTime,
  duration,
  onPrevious,
  onNext,
  onClose,
}: {
  t: T;
  client: ResultsClient;
  surveyId: string;
  responseId: string;
  statusText: (status: ResponseRow["status"]) => string;
  dateTime: Intl.DateTimeFormat;
  duration: (seconds: number | null) => string;
  onPrevious: (() => void) | undefined;
  onNext: (() => void) | undefined;
  onClose: () => void;
}) {
  const [detail, setDetail] = useState<ResponseDetail | null>(null);
  const panel = useRef<HTMLElement>(null);

  useEffect(() => {
    let current = true;
    setDetail(null);
    client.response(surveyId, responseId).then(
      (answer) => current && setDetail(answer),
      () => undefined,
    );

    return () => {
      current = false;
    };
  }, [client, surveyId, responseId]);

  useEffect(() => {
    panel.current?.focus();
  }, []);

  return (
    <aside
      ref={panel}
      className="results-panel"
      aria-label={t("response")}
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          onClose();
        }
      }}
    >
      <div className="results-panel__head">
        <h2>{t("response")}</h2>
        <span className="designer-bar__grow" />
        <button type="button" className="results-icon" aria-label={t("previous")} disabled={onPrevious === undefined} onClick={onPrevious}>
          ‹
        </button>
        <button type="button" className="results-icon" aria-label={t("next")} disabled={onNext === undefined} onClick={onNext}>
          ›
        </button>
        <button type="button" className="results-icon" aria-label={t("close")} onClick={onClose}>
          ✕
        </button>
      </div>
      {detail === null ? (
        <div aria-busy="true">
          <Skeleton width="80%" height="14px" />
          <Skeleton width="60%" height="14px" />
        </div>
      ) : (
        <>
          <p className="results-card__muted">
            {[statusText(detail.status), t("versionShort", { number: detail.version }), detail.language.toUpperCase(), dateTime.format(new Date(detail.completedAt ?? detail.startedAt)), duration(detail.durationSeconds)]
              .filter((part) => part !== "—")
              .join(" · ")}
          </p>
          <dl className="results-answers">
            {detail.answers.map((answer, position) => (
              <div key={`${answer.id}-${position}`}>
                <dt>{answer.text}</dt>
                <dd>{answer.value ?? "—"}</dd>
              </div>
            ))}
          </dl>
        </>
      )}
    </aside>
  );
}
