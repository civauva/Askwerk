// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type ExportFormat, type ExportItem, type ExportRequest, type ResultsClient, type ResultsFilter } from "@askwerk/app-client";
import { DataGrid, dataGridTexts, type DataGridColumn, Dialog } from "@askwerk/ui";
import { useEffect, useId, useRef, useState } from "react";
import type { ResultsMessageKey } from "./messages";

type T = (key: ResultsMessageKey, values?: Record<string, string | number>) => string;

type ExportDialogProps = {
  t: T;
  locale: "de" | "en";
  client: ResultsClient;
  surveyId: string;
  /** The results' filter; the export follows it. */
  filter: ResultsFilter;
  language: string;
  /** Responses the filter selects. */
  count: number;
  timeZone?: string | undefined;
  /** How often a running export is asked about. */
  pollMs: number;
  /** Closes the dialog; with the export when it is still being prepared, so the page can say when it is ready. */
  onClose: (waiting: ExportItem | null) => void;
};

type State = { kind: "choosing" } | { kind: "starting" } | { kind: "export"; item: ExportItem } | { kind: "refused"; message: string };

const formats: readonly (readonly [ExportFormat, ResultsMessageKey, ResultsMessageKey])[] = [
  ["excel", "formatExcel", "formatExcelHint"],
  ["csv", "formatCsv", "formatCsvHint"],
  ["spss", "formatSpss", "formatSpssHint"],
  ["stata", "formatStata", "formatStataHint"],
  ["json", "formatJson", "formatJsonHint"],
];

const formatNames: Record<ExportFormat, ResultsMessageKey> = { excel: "formatExcel", csv: "formatCsv", spss: "formatSpss", stata: "formatStata", json: "formatJson" };

/** How big a file is, in KB or MB. */
export function fileSize(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const running = (item: ExportItem) => item.status === "queued" || item.status === "running";

/**
 * Export (AWK-24, W11 C; AWK-25, W14 A): the format first, Excel recommended; layout and values for CSV and Excel
 * behind More; the survey's recent exports below. A job prepares the file: the dialog shows how far it is, may be
 * closed meanwhile, and offers the download once it is ready.
 */
export function ExportDialog({ t, locale, client, surveyId, filter, language, count, timeZone, pollMs, onClose }: ExportDialogProps) {
  const titleId = useId();
  const [format, setFormat] = useState<ExportFormat>("excel");
  const [layout, setLayout] = useState<"wide" | "long">("wide");
  const [values, setValues] = useState<"labels" | "codes" | "both">("labels");
  const [more, setMore] = useState(false);
  const [state, setState] = useState<State>({ kind: "choosing" });
  const [recent, setRecent] = useState<ExportItem[] | null>(null);
  const first = useRef<HTMLInputElement>(null);
  const table = format === "excel" || format === "csv";
  const zone = timeZone === undefined ? {} : { timeZone };
  const number = new Intl.NumberFormat(locale);
  const day = new Intl.DateTimeFormat(locale, { dateStyle: "medium", ...zone });
  const clock = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...zone });
  const when = new Intl.DateTimeFormat(locale, { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...zone });

  useEffect(() => {
    first.current?.focus();
    client.recentExports(surveyId).then(setRecent, () => setRecent([]));
  }, [client, surveyId]);

  // Asks about a running export until it is ready, failed or expired.
  const watched = state.kind === "export" && running(state.item) ? state.item : null;

  useEffect(() => {
    if (watched === null) {
      return;
    }

    let current = true;
    const timer = setTimeout(() => {
      client.exportOf(watched.id).then(
        (item) => current && setState({ kind: "export", item }),
        () => undefined,
      );
    }, pollMs);

    return () => {
      current = false;
      clearTimeout(timer);
    };
  }, [client, watched, pollMs]);

  const start = () => {
    const request: ExportRequest = { ...filter, format, language, ...(table ? { layout, values } : {}) };
    setState({ kind: "starting" });
    client.startExport(surveyId, request).then(
      (item) => setState({ kind: "export", item }),
      (error: unknown) => {
        const code = error instanceof ApiError ? error.problem.code : undefined;
        setState({
          kind: "refused",
          message: code === "role-not-allowed" ? t("exportNotAllowed") : code === "too-many-exports" ? t("tooManyExports") : t("exportFailed"),
        });
      },
    );
  };

  const close = () => onClose(state.kind === "export" && running(state.item) ? state.item : null);

  const radio = (name: string, value: string, checked: boolean, onChange: () => void, label: string) => (
    <label key={value} className="results-radio">
      <input type="radio" name={name} value={value} checked={checked} onChange={onChange} />
      <span>{label}</span>
    </label>
  );

  const recentColumns: DataGridColumn<ExportItem>[] = [
    { key: "file", header: t("exportFile"), phone: "title", cell: (item) => item.file?.name ?? t(formatNames[item.format]) },
    { key: "by", header: t("exportBy"), cell: (item) => (item.byApiKey ? t("byApiKey", { name: item.requestedBy }) : item.requestedBy) },
    { key: "started", header: t("exportStarted"), align: "end", cell: (item) => when.format(new Date(item.createdAt)) },
    { key: "size", header: t("exportSize"), align: "end", cell: (item) => (item.file === null ? "" : fileSize(item.file.size)) },
    {
      key: "state",
      header: t("exportState"),
      align: "end",
      cell: (item) =>
        item.status === "ready" ? (
          <a href={client.exportFileUrl(item.id)} download={item.file?.name}>
            {t("download")}
          </a>
        ) : running(item) ? (
          `${item.total === 0 ? 0 : Math.floor((item.done / item.total) * 100)} %`
        ) : item.status === "failed" ? (
          t("exportFailedShort")
        ) : (
          t("exportExpired")
        ),
    },
  ];

  return (
    <Dialog title={t("exportTitle")} onClose={close} wide className="results-dialog">
        {state.kind === "choosing" && (
          <>
            <fieldset className="results-fieldset">
              <legend>{t("format")}</legend>
              {formats.map(([value, label, hint], index) => (
                <label key={value} className="results-radio results-radio--format">
                  <input ref={index === 0 ? first : undefined} type="radio" name="format" value={value} checked={format === value} onChange={() => setFormat(value)} />
                  <span>
                    {t(label)}
                    <small>{t(hint)}</small>
                  </span>
                </label>
              ))}
            </fieldset>
            <p className="results-dialog__label">{t("exportResponses")}</p>
            <p>{count === 1 ? t("exportOne") : t("exportMany", { count })}</p>
            <button type="button" className="results-link-button" aria-expanded={more} onClick={() => setMore((open) => !open)}>
              {t("more")}
              <span aria-hidden="true"> {more ? "▴" : "▾"}</span>
            </button>
            {more &&
              (table ? (
                <div className="results-more-options">
                  <fieldset className="results-fieldset">
                    <legend>{t("layout")}</legend>
                    {radio("layout", "wide", layout === "wide", () => setLayout("wide"), t("layoutWide"))}
                    {radio("layout", "long", layout === "long", () => setLayout("long"), t("layoutLong"))}
                  </fieldset>
                  <fieldset className="results-fieldset">
                    <legend>{t("values")}</legend>
                    {radio("values", "labels", values === "labels", () => setValues("labels"), t("valuesLabels"))}
                    {radio("values", "codes", values === "codes", () => setValues("codes"), t("valuesCodes"))}
                    {radio("values", "both", values === "both", () => setValues("both"), t("valuesBoth"))}
                  </fieldset>
                </div>
              ) : (
                <p className="results-card__muted">{t("exportFixed")}</p>
              ))}
            <div className="results-dialog__buttons">
              <button type="button" className="designer-button designer-button--quiet" onClick={close}>
                {t("cancel")}
              </button>
              <button type="button" className="designer-button results-primary" onClick={start}>
                {t("export")}
              </button>
            </div>
            {recent !== null && recent.length > 0 && (
              <section className="results-recent" aria-labelledby={`${titleId}-recent`}>
                <h3 id={`${titleId}-recent`} className="results-dialog__label">
                  {t("recentExports")}
                </h3>
                <DataGrid label={t("recentExports")} columns={recentColumns} rows={recent} rowKey={(item) => item.id} texts={dataGridTexts(locale)} />
              </section>
            )}
          </>
        )}
        {(state.kind === "starting" || (state.kind === "export" && running(state.item))) && (
          <Preparing t={t} item={state.kind === "export" ? state.item : null} number={number} clock={clock} onClose={close} />
        )}
        {state.kind === "export" && state.item.status === "ready" && state.item.file !== null && (
          <div className="results-job">
            <p className="results-job__title">{t("readyTitle")}</p>
            <div className="results-job__file">
              <b>{state.item.file.name}</b>
              <span className="results-card__muted">{state.item.total === 1 ? t("readyMetaOne", { size: fileSize(state.item.file.size) }) : t("readyMeta", { count: number.format(state.item.total), size: fileSize(state.item.file.size) })}</span>
            </div>
            {state.item.expiresAt !== null && <p className="results-card__muted">{t("keptUntil", { date: day.format(new Date(state.item.expiresAt)) })}</p>}
            <div className="results-dialog__buttons">
              <button type="button" className="designer-button designer-button--quiet" onClick={close}>
                {t("close")}
              </button>
              <a className="designer-button results-primary" href={client.exportFileUrl(state.item.id)} download={state.item.file.name}>
                {t("download")}
              </a>
            </div>
          </div>
        )}
        {(state.kind === "refused" || (state.kind === "export" && (state.item.status === "failed" || state.item.status === "expired"))) && (
          <div className="results-job">
            <p role="alert">
              {state.kind === "refused" ? state.message : state.kind === "export" && state.item.error === "too-many-rows-for-excel" ? t("exportTooManyRows") : t("exportFailed")}
            </p>
            <div className="results-dialog__buttons">
              <button type="button" className="designer-button designer-button--quiet" onClick={close}>
                {t("close")}
              </button>
              <button type="button" className="designer-button" onClick={() => setState({ kind: "choosing" })}>
                {t("tryAgain")}
              </button>
            </div>
          </div>
        )}
    </Dialog>
  );
}

/** While the job works: how far it is, as a measuring rule, or when it is tried again. */
function Preparing({ t, item, number, clock, onClose }: { t: T; item: ExportItem | null; number: Intl.NumberFormat; clock: Intl.DateTimeFormat; onClose: () => void }) {
  const percent = item === null || item.total === 0 ? 0 : Math.floor((item.done / item.total) * 100);

  return (
    <div className="results-job">
      <p className="results-job__title">{t("preparing")}</p>
      <span className="results-job__rule" role="progressbar" aria-label={t("preparing")} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
        <span style={{ width: `${percent}%` }} />
      </span>
      {item !== null && item.total > 0 && <p className="results-job__count">{t("preparingProgress", { done: number.format(item.done), total: number.format(item.total) })}</p>}
      {item?.attempt != null && item.retryAt !== null && <p role="status">{t("retrying", { time: clock.format(new Date(item.retryAt)), next: item.attempt + 1 })}</p>}
      <p className="results-card__muted">{t("preparingHint")}</p>
      <div className="results-dialog__buttons">
        <button type="button" className="designer-button designer-button--quiet" onClick={onClose}>
          {t("close")}
        </button>
      </div>
    </div>
  );
}
