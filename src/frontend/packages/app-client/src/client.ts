// SPDX-License-Identifier: AGPL-3.0-only
import { newTraceparent, type Telemetry } from "@askwerk/telemetry";
import type { components } from "./schema";

export type StatusResponse = components["schemas"]["StatusResponse"];

/** What the API explains in a problem answer: a code, field errors, sign-in details and publish findings. */
export interface Problem {
  readonly code?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
  readonly issues?: readonly { readonly code: string; readonly severity: string; readonly path: string; readonly language?: string | null }[];
  readonly triesLeft?: number | null;
  readonly lockedUntil?: string | null;
}

/** A failed App API call. status 0 means the network failed before any answer. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly traceId: string | undefined,
    message: string,
    readonly problem: Problem = {},
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export type AppClientOptions = {
  fetch?: typeof fetch;
  telemetry: Telemetry;
  /** Milliseconds clock for call durations; defaults to performance.now. */
  now?: () => number;
  baseUrl?: string;
  /** Headers for every call, e.g. an embedded session's bearer token (AWK-13); read on each call. */
  headers?: () => Record<string, string>;
};

export type AppClient = {
  getStatus(): Promise<StatusResponse>;
};

export type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

/** One call to the App API; resolves with the JSON body, or undefined when the answer has none. */
export type Request = <T>(method: HttpMethod, path: string, body?: unknown) => Promise<T>;

/** A file the App API answered with, and the name the server gave it. */
export type DownloadedFile = { readonly blob: Blob; readonly fileName: string };

/** One call to the App API that answers with a file (AWK-24, exports). */
export type Download = (method: HttpMethod, path: string, body?: unknown) => Promise<DownloadedFile>;

/**
 * The designer's and runner's only way to the App API: types from the App API's OpenAPI document, a
 * traceparent on every call, and failures and timings reported to telemetry (agents/observability.md).
 */
export function createRequest(options: AppClientOptions): Request {
  const send = createSend(options);

  return async <T>(method: HttpMethod, path: string, body?: unknown): Promise<T> => {
    const text = await (await send(method, path, body, "application/json")).text();

    return (text === "" ? undefined : JSON.parse(text)) as T;
  };
}

/** Like createRequest, for answers that are files: the body as a blob and the name from Content-Disposition. */
export function createDownload(options: AppClientOptions): Download {
  const send = createSend(options);

  return async (method, path, body) => {
    const response = await send(method, path, body, "*/*");

    return { blob: await response.blob(), fileName: fileNameOf(response.headers.get("Content-Disposition")) };
  };
}

/** The file name of a Content-Disposition header; filename* (RFC 5987, UTF-8) wins over filename. */
function fileNameOf(header: string | null): string {
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header ?? "")?.[1];

  if (encoded !== undefined) {
    return decodeURIComponent(encoded.trim());
  }

  return /filename="?([^";]+)"?/i.exec(header ?? "")?.[1]?.trim() ?? "download";
}

function createSend(options: AppClientOptions) {
  const fetchFunction = options.fetch ?? globalThis.fetch.bind(globalThis);
  const now = options.now ?? (() => performance.now());
  const baseUrl = options.baseUrl ?? "";

  return async (method: HttpMethod, path: string, body: unknown, accept: string): Promise<Response> => {
    const { traceparent, traceId } = newTraceparent();
    const startedAt = now();
    const headers: Record<string, string> = { ...options.headers?.(), traceparent, Accept: accept };
    let response: Response;

    // Uploads go as multipart form data; the browser sets that content type with its boundary.
    const isForm = typeof FormData !== "undefined" && body instanceof FormData;

    if (body !== undefined && !isForm) {
      headers["Content-Type"] = "application/json";
    }

    try {
      response = await fetchFunction(baseUrl + path, {
        method,
        headers,
        credentials: "same-origin",
        ...(body !== undefined && { body: isForm ? (body as FormData) : JSON.stringify(body) }),
      });
    } catch {
      options.telemetry.error("api.failed", `${method} ${path} network error`, traceId);
      throw new ApiError(0, traceId, "The server could not be reached.");
    }

    options.telemetry.metric("api.duration", now() - startedAt);

    if (!response.ok) {
      const problem = await readProblem(response);
      const serverTraceId = typeof problem.traceId === "string" ? problem.traceId : undefined;
      options.telemetry.error("api.failed", `${method} ${path} ${response.status}`, serverTraceId ?? traceId);
      throw new ApiError(response.status, serverTraceId ?? traceId, `${method} ${path} failed with ${response.status}.`, problem);
    }

    return response;
  };
}

export function createAppClient(options: AppClientOptions): AppClient {
  const request = createRequest(options);

  return {
    getStatus: () => request<StatusResponse>("GET", "/api/app/status"),
  };
}

async function readProblem(response: Response): Promise<Problem & { traceId?: unknown }> {
  try {
    const problem = (await response.json()) as unknown;

    return typeof problem === "object" && problem !== null ? (problem as Problem & { traceId?: unknown }) : {};
  } catch {
    return {};
  }
}
