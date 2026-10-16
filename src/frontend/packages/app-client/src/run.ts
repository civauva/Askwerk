// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, createRequest, type AppClientOptions } from "./client";

/** One answer as the runner sends it; which fields count depends on the question type (AWK-6). */
export interface AnswerValue {
  readonly option?: string;
  readonly options?: readonly string[];
  readonly other?: string;
  readonly number?: number;
  readonly text?: string;
  readonly date?: string;
  readonly rows?: Readonly<Record<string, string>>;
  readonly order?: readonly string[];
  readonly dontKnow?: boolean;
}

export type Answers = Readonly<Record<string, AnswerValue>>;

export interface RunResponse {
  readonly status: "partial" | "complete";
  readonly pageId: string;
  readonly language: string;
  readonly answers: Answers;
}

/** What opening a link gives; the definition stays opaque here, packages/schema types it. */
export interface RunSurvey<TDefinition = unknown> {
  readonly survey: {
    readonly id: string;
    readonly organisation: string;
    readonly state: "open" | "closed";
    readonly definition: TDefinition;
    /** The look (AWK-17); packages/schema types it as ThemeTokens. */
    readonly theme?: unknown;
    /** When it closed by its closing date (W24), ISO 8601; absent otherwise. */
    readonly closedAt?: string | null;
    /** The organisation's time zone, for showing that date. */
    readonly timeZone?: string;
    /** Whether a browser that finished may not answer again (W24); on when absent. */
    readonly oneResponsePerPerson?: boolean;
  };
  readonly response: RunResponse | null;
}

/** The runner's calls (AWK-6). A survey that is not published answers null; errors are ApiError with the problems per question. */
export interface RunClient {
  open<TDefinition = unknown>(surveyId: string): Promise<RunSurvey<TDefinition> | null>;
  start(surveyId: string, language: string): Promise<RunResponse>;
  savePage(surveyId: string, pageId: string, answers: Answers): Promise<void>;
  complete(surveyId: string): Promise<void>;
  /** Where the browser loads a file of the survey's look; a width asks for a smaller copy of an image. */
  assetUrl(surveyId: string, assetId: string, width?: number): string;
}

const widthQuery = (width?: number) => (width === undefined ? "" : `?w=${width}`);

const surveyIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The survey id behind the link's segment (AWK-7): links carry a short code (/s/kz26b7mq), older ones the id. An
 * unknown code comes back unchanged, so the runner answers "not found" as for any unknown survey.
 */
export async function surveyIdOf(options: AppClientOptions, segment: string): Promise<string> {
  if (surveyIdPattern.test(segment)) {
    return segment;
  }

  try {
    const found = await createRequest(options)<{ surveyId: string }>("GET", `/api/app/run/code/${encodeURIComponent(segment)}`);
    return found.surveyId;
  } catch {
    return segment;
  }
}

/**
 * The runner's calls for a published survey. A personal link's token (AWK-7, /s/{code}?t=…) goes with opening and
 * starting, so the response stays with the invited person.
 */
export function createRunClient(options: AppClientOptions, participantToken?: string, identity?: string): RunClient {
  const request = createRequest(options);
  const at = (surveyId: string) => `/api/app/run/${encodeURIComponent(surveyId)}`;
  const token = participantToken === undefined || participantToken === "" ? undefined : participantToken;

  return {
    open: <TDefinition>(surveyId: string) =>
      request<RunSurvey<TDefinition>>("GET", `${at(surveyId)}${token === undefined ? "" : `?t=${encodeURIComponent(token)}`}`).catch((error: unknown) => {
        if (error instanceof ApiError && error.status === 404) {
          return null;
        }

        throw error;
      }),
    start: (surveyId, language) => request<RunResponse>("POST", `${at(surveyId)}/response`, { language, ...(token === undefined ? {} : { token }), ...(identity === undefined || identity === "" ? {} : { identity }) }),
    savePage: (surveyId, pageId, answers) => request("PUT", `${at(surveyId)}/response/pages/${encodeURIComponent(pageId)}`, { answers }),
    complete: (surveyId) => request("POST", `${at(surveyId)}/response/complete`),
    assetUrl: (surveyId, assetId, width) => `${at(surveyId)}/assets/${encodeURIComponent(assetId)}${widthQuery(width)}`,
  };
}

/** Whose unsubscribe link it is (AWK-7): the organisation and the address, partly hidden. */
export interface UnsubscribeInfo {
  readonly organisation: string;
  readonly email: string;
  readonly language: string | null;
  readonly done: boolean;
}

/** Reads an unsubscribe link; 404 for an unknown one. */
export function unsubscribeInfo(options: AppClientOptions, token: string): Promise<UnsubscribeInfo> {
  return createRequest(options)<UnsubscribeInfo>("GET", `/api/app/run/unsubscribe/${encodeURIComponent(token)}`);
}

/** No more invitations from that organisation to that address. */
export function unsubscribe(options: AppClientOptions, token: string): Promise<void> {
  return createRequest(options)("POST", `/api/app/run/unsubscribe/${encodeURIComponent(token)}`);
}

/**
 * The runner's calls in preview (AWK-22, W9): the last saved draft, for editors of its organisation only. Pages are
 * checked like in the real survey, but nothing is started or stored; anyone else gets "not found", like a dead link.
 */
export function createPreviewClient(options: AppClientOptions): RunClient {
  const request = createRequest(options);
  const at = (surveyId: string) => `/api/app/surveys/${encodeURIComponent(surveyId)}/preview`;
  let firstPage = "";

  return {
    open: async <TDefinition>(surveyId: string) => {
      try {
        const opened = await request<RunSurvey<TDefinition>>("GET", at(surveyId));
        firstPage = (opened.survey.definition as { pages?: { id: string }[] }).pages?.[0]?.id ?? "";

        return opened;
      } catch (error) {
        if (error instanceof ApiError && [401, 403, 404].includes(error.status)) {
          return null;
        }

        throw error;
      }
    },
    start: (_surveyId, language) => Promise.resolve<RunResponse>({ status: "partial", pageId: firstPage, language, answers: {} }),
    savePage: (surveyId, pageId, answers) => request("PUT", `${at(surveyId)}/pages/${encodeURIComponent(pageId)}`, { answers }),
    complete: () => Promise.resolve(),
    // The editor is signed in; the draft's files come from the organisation's own address.
    assetUrl: (_surveyId, assetId, width) => `/api/app/assets/${encodeURIComponent(assetId)}${widthQuery(width)}`,
  };
}
