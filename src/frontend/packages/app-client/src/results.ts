// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** Which responses count (W11, the filter chips); empty means all. */
export interface ResultsFilter {
  readonly status?: "complete" | "partial";
  /** Started at or after (ISO, UTC). */
  readonly from?: string;
  /** Started before (ISO, UTC). */
  readonly to?: string;
  readonly version?: number;
}

export interface ResultTotals {
  readonly responses: number;
  readonly complete: number;
  readonly partial: number;
  readonly medianSeconds: number | null;
  readonly lastAt: string | null;
}

export interface OptionCount {
  readonly code: string;
  readonly label: string;
  readonly count: number;
  /** Ranking: the average place, 1 = first. */
  readonly averagePosition: number | null;
}

/** The summary of one question; only the parts its type has are set. */
export interface QuestionResult {
  readonly id: string;
  readonly variable: string;
  readonly type: string;
  readonly text: string;
  readonly answered: number;
  readonly dontKnow: number;
  readonly options: readonly OptionCount[] | null;
  readonly numbers: { readonly average: number; readonly median: number; readonly min: number; readonly max: number } | null;
  readonly distribution: readonly { readonly value: number; readonly count: number }[] | null;
  readonly nps: { readonly score: number; readonly detractors: number; readonly passives: number; readonly promoters: number } | null;
  readonly rows: readonly { readonly code: string; readonly label: string; readonly options: readonly OptionCount[] }[] | null;
  readonly latest: readonly { readonly text: string; readonly at: string }[] | null;
  readonly dates: { readonly earliest: string; readonly latest: string } | null;
}

export interface Results {
  readonly title: string;
  readonly languages: readonly string[];
  readonly defaultLanguage: string;
  /** False until the survey is published; there are no results before. */
  readonly published: boolean;
  readonly totals: ResultTotals;
  readonly versions: readonly number[];
  readonly questions: readonly QuestionResult[];
}

export interface ResponseRow {
  readonly id: string;
  readonly status: "complete" | "partial";
  readonly version: number;
  readonly language: string;
  readonly startedAt: string;
  readonly completedAt: string | null;
  readonly durationSeconds: number | null;
  /** Answers as text, by variable name. */
  readonly answers: Readonly<Record<string, string>>;
}

export interface ResponsePage {
  readonly total: number;
  readonly columns: readonly { readonly id: string; readonly variable: string; readonly type: string; readonly text: string }[];
  readonly items: readonly ResponseRow[];
  /** The cursor of the next page; null on the last. */
  readonly next: string | null;
}

export interface ResponseDetail extends Omit<ResponseRow, "answers"> {
  readonly answers: readonly { readonly id: string; readonly variable: string; readonly text: string; readonly value: string | null }[];
}

/** order: oldest for oldest first; newest first when left out. */
export type ResponseQuery = ResultsFilter & { readonly search?: string; readonly cursor?: string; readonly limit?: number; readonly order?: "newest" | "oldest" };

export type ExportFormat = "excel" | "csv" | "spss" | "stata" | "json";

/** An export (W11 C): the format, layout and values for CSV and Excel, the results' filter and the labels' language. */
export type ExportRequest = ResultsFilter & {
  readonly format: ExportFormat;
  readonly layout?: "wide" | "long";
  readonly values?: "labels" | "codes" | "both";
  readonly language?: string;
};

/** An export prepared by a job (AWK-25, W14 A). */
export interface ExportItem {
  readonly id: string;
  readonly surveyId: string;
  readonly status: "queued" | "running" | "ready" | "failed" | "expired";
  readonly format: ExportFormat;
  /** Responses written of all to write. */
  readonly done: number;
  readonly total: number;
  readonly file: { readonly name: string; readonly contentType: string; readonly size: number } | null;
  /** Why it failed: export-failed or too-many-rows-for-excel. */
  readonly error: string | null;
  /** The person or key who asked. */
  readonly requestedBy: string;
  readonly byApiKey: boolean;
  /** Failed tries so far, while it will be tried again. */
  readonly attempt: number | null;
  readonly retryAt: string | null;
  readonly createdAt: string;
  readonly finishedAt: string | null;
  /** When the file is deleted. */
  readonly expiresAt: string | null;
}

/** Results of a survey (AWK-24, W11): the overview, responses a page at a time, one response. */
export interface ResultsClient {
  overview(surveyId: string, filter: ResultsFilter, language: string): Promise<Results>;
  responses(surveyId: string, query: ResponseQuery, language: string): Promise<ResponsePage>;
  response(surveyId: string, responseId: string): Promise<ResponseDetail>;
  /** Asks for an export; a job prepares it. 403 role-not-allowed, 409 not-published, 429 too-many-exports. */
  startExport(surveyId: string, request: ExportRequest): Promise<ExportItem>;
  exportOf(exportId: string): Promise<ExportItem>;
  /** The survey's exports of the last seven days, newest first. */
  recentExports(surveyId: string): Promise<ExportItem[]>;
  /** Where the browser downloads an export's file (same session; audited). */
  exportFileUrl(exportId: string): string;
}

function queryOf(values: Record<string, string | number | undefined>) {
  const query = new URLSearchParams();

  for (const [key, value] of Object.entries(values)) {
    const text = value === undefined ? "" : String(value).trim();
    text && query.set(key, text);
  }

  return query.toString();
}

export function createResultsClient(options: AppClientOptions): ResultsClient {
  const request = createRequest(options);
  const at = (surveyId: string) => `/api/app/surveys/${encodeURIComponent(surveyId)}`;
  const filter = ({ status, from, to, version }: ResultsFilter) => ({ status, from, to, version });

  return {
    overview: (surveyId, query, language) => request<Results>("GET", `${at(surveyId)}/results?${queryOf({ ...filter(query), language })}`),
    responses: (surveyId, query, language) =>
      request<ResponsePage>("GET", `${at(surveyId)}/responses?${queryOf({ ...filter(query), search: query.search, cursor: query.cursor, limit: query.limit, order: query.order === "oldest" ? "oldest" : undefined, language })}`),
    response: (surveyId, responseId) => request<ResponseDetail>("GET", `${at(surveyId)}/responses/${encodeURIComponent(responseId)}`),
    startExport: (surveyId, body) => request<ExportItem>("POST", `${at(surveyId)}/exports`, body),
    exportOf: (exportId) => request<ExportItem>("GET", `/api/app/exports/${encodeURIComponent(exportId)}`),
    recentExports: (surveyId) => request<{ items: ExportItem[] }>("GET", `${at(surveyId)}/exports`).then((answer) => answer.items),
    exportFileUrl: (exportId) => `/api/app/exports/${encodeURIComponent(exportId)}/file`,
  };
}
