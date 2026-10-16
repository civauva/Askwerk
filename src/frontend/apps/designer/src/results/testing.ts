// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written ResultsClient for the Results page's tests: three responses to a short survey.
import { ApiError, type ExportItem, type ExportRequest, type QuestionResult, type ResponseDetail, type ResponsePage, type ResponseQuery, type ResponseRow, type Results, type ResultsClient, type ResultsFilter } from "@askwerk/app-client";

const question = (part: Partial<QuestionResult> & Pick<QuestionResult, "id" | "variable" | "type" | "text" | "answered">): QuestionResult => ({
  dontKnow: 0,
  options: null,
  numbers: null,
  distribution: null,
  nps: null,
  rows: null,
  latest: null,
  dates: null,
  ...part,
});

export const results: Results = {
  title: "Customer satisfaction 2026",
  languages: ["de", "en"],
  defaultLanguage: "de",
  published: true,
  totals: { responses: 3, complete: 2, partial: 1, medianSeconds: 250, lastAt: "2026-10-14T10:42:00Z" },
  versions: [1, 2],
  questions: [
    question({
      id: "q1",
      variable: "SOURCE",
      type: "single",
      text: "How did you hear about us?",
      answered: 3,
      options: [
        { code: "1", label: "Search engine", count: 2, averagePosition: null },
        { code: "2", label: "Advertising", count: 0, averagePosition: null },
        { code: "OTHER", label: "Other", count: 1, averagePosition: null },
      ],
    }),
    question({
      id: "q2",
      variable: "SAT",
      type: "rating",
      text: "How satisfied are you overall?",
      answered: 3,
      numbers: { average: 3.6667, median: 4, min: 2, max: 5 },
      distribution: [1, 2, 3, 4, 5].map((value) => ({ value, count: value === 2 || value >= 4 ? 1 : 0 })),
    }),
    question({ id: "q3", variable: "IMPROVE", type: "long-text", text: "What could we improve?", answered: 5, latest: [{ text: "Parking was hard to find.", at: "2026-10-14T08:03:00Z" }, { text: "Shorter waiting times.", at: "2026-10-13T10:00:00Z" }] }),
    question({
      id: "q4",
      variable: "ASPECTS",
      type: "matrix",
      text: "How do you rate?",
      answered: 2,
      rows: [{ code: "a", label: "Price", options: [{ code: "1", label: "Good", count: 1, averagePosition: null }, { code: "2", label: "Poor", count: 1, averagePosition: null }] }],
    }),
    question({ id: "q5", variable: "NPS", type: "nps", text: "Would you recommend us?", answered: 2, numbers: { average: 8, median: 8, min: 6, max: 10 }, distribution: [], nps: { score: 0, detractors: 1, passives: 0, promoters: 1 } }),
  ],
};

const row = (id: string, status: ResponseRow["status"], startedAt: string, answers: Record<string, string>): ResponseRow => ({
  id,
  status,
  version: 2,
  language: "en",
  startedAt,
  completedAt: status === "complete" ? startedAt.replace(":00Z", ":30Z") : null,
  durationSeconds: status === "complete" ? 190 : null,
  answers,
});

export const rows: ResponseRow[] = [
  row("r3", "partial", "2026-10-14T10:42:00Z", { SOURCE: "Advertising", SAT: "2" }),
  row("r2", "complete", "2026-10-14T08:03:00Z", { SOURCE: "Search engine", SAT: "4", IMPROVE: "Parking was hard to find." }),
  row("r1", "complete", "2026-10-13T10:00:00Z", { SOURCE: "Other: Radio", SAT: "5", IMPROVE: "Shorter waiting times." }),
];

const columns = results.questions.map(({ id, variable, type, text }) => ({ id, variable, type, text }));

export class FakeResultsClient implements ResultsClient {
  readonly calls: [string, ...unknown[]][] = [];
  answer: Results = results;
  failOverview = false;
  /** Responses per page, so "more" is needed after the first. */
  pageSize = 2;
  exportAnswer: "ok" | ApiError = "ok";
  /** What polling the export answers, one after the other; the last one stays. */
  exportSteps: Partial<ExportItem>[] = [{ status: "running", done: 1, total: 2 }, readyExport];
  recent: ExportItem[] = [];

  overview = (surveyId: string, filter: ResultsFilter, language: string) => {
    this.calls.push(["overview", surveyId, filter, language]);
    return this.failOverview ? Promise.reject(new Error("offline")) : Promise.resolve(this.answer);
  };

  responses = (surveyId: string, query: ResponseQuery, language: string) => {
    this.calls.push(["responses", surveyId, query, language]);
    const chosen = rows.filter((each) => (query.status === undefined || each.status === query.status) && (query.search === undefined || Object.values(each.answers).some((text) => text.toLowerCase().includes(query.search!.toLowerCase()))));
    const start = query.cursor === undefined ? 0 : Number(query.cursor);
    const page: ResponsePage = { total: chosen.length, columns, items: chosen.slice(start, start + this.pageSize), next: start + this.pageSize < chosen.length ? String(start + this.pageSize) : null };

    return Promise.resolve(page);
  };

  response = (surveyId: string, responseId: string) => {
    this.calls.push(["response", surveyId, responseId]);
    const found = rows.find((each) => each.id === responseId)!;
    const detail: ResponseDetail = {
      ...found,
      answers: columns.map((column) => ({ id: column.id, variable: column.variable, text: column.text, value: found.answers[column.variable] ?? null })),
    };

    return Promise.resolve(detail);
  };

  startExport = (surveyId: string, request: ExportRequest) => {
    this.calls.push(["startExport", surveyId, request]);

    return this.exportAnswer === "ok" ? Promise.resolve(exportItem({ format: request.format })) : Promise.reject(this.exportAnswer);
  };

  exportOf = (exportId: string) => {
    this.calls.push(["exportOf", exportId]);
    const step = this.exportSteps.length > 1 ? this.exportSteps.shift()! : this.exportSteps[0]!;

    return Promise.resolve(exportItem(step));
  };

  recentExports = (surveyId: string) => {
    this.calls.push(["recentExports", surveyId]);
    return Promise.resolve(this.recent);
  };

  exportFileUrl = (exportId: string) => `/api/app/exports/${exportId}/file`;
}

/** An export as the server describes it; queued unless a test says otherwise. */
export function exportItem(part: Partial<ExportItem> = {}): ExportItem {
  return {
    id: "e1",
    surveyId: "s1",
    status: "queued",
    format: "excel",
    done: 0,
    total: 0,
    file: null,
    error: null,
    requestedBy: "Anna Berger",
    byApiKey: false,
    attempt: null,
    retryAt: null,
    createdAt: "2026-10-15T09:12:00Z",
    finishedAt: null,
    expiresAt: null,
    ...part,
  };
}

export const readyExport: Partial<ExportItem> = {
  status: "ready",
  done: 2,
  total: 2,
  file: { name: "customer-satisfaction-2026.xlsx", contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", size: 2048 },
  finishedAt: "2026-10-15T09:13:00Z",
  expiresAt: "2026-10-22T09:13:00Z",
};

export function exportNotAllowed(): ApiError {
  return new ApiError(403, "4bf92f3577b34da6a3ce929d0e0e4736", "forbidden", { code: "role-not-allowed" });
}
