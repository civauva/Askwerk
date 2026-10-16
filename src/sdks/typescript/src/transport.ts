// SPDX-License-Identifier: Apache-2.0
// The hand-written part of the TypeScript SDK: authentication, requests, errors, retries and paging.

/** An API key, or an OAuth client whose access tokens are fetched and renewed as needed. */
export type Credentials = { apiKey: string } | { clientId: string; clientSecret: string; scope?: string };

/** Every error answer of the Public API, with its problem details. */
export class AskwerkError extends Error {
  readonly status: number;
  /** The stable problem type, e.g. https://askwerk.org/problems/not-published. */
  readonly type: string | undefined;
  readonly title: string | undefined;
  readonly detail: string | undefined;
  /** Helps an administrator find the call in the logs. */
  readonly traceId: string | undefined;

  constructor(status: number, problem: { type?: string; title?: string; detail?: string; traceId?: string; error?: string; error_description?: string }) {
    super(problem.title ?? problem.error_description ?? `HTTP ${status}`);
    this.name = "AskwerkError";
    this.status = status;
    this.type = problem.type ?? problem.error;
    this.title = problem.title ?? problem.error_description;
    this.detail = problem.detail;
    this.traceId = problem.traceId;
  }
}

type Options = { query?: Record<string, string | number | boolean | undefined | null>; body?: unknown; idempotencyKey?: string; binary?: boolean };

const maxRetries = 3;

export class Transport {
  readonly #baseUrl: string;
  readonly #credentials: Credentials;
  readonly #fetch: typeof fetch;
  #token: { value: string; until: number } | undefined;

  constructor(baseUrl: string, credentials: Credentials, fetcher: typeof fetch = fetch) {
    this.#baseUrl = baseUrl.replace(/\/+$/, "");
    this.#credentials = credentials;
    this.#fetch = fetcher;
  }

  async request(method: string, path: string, options: Options = {}): Promise<unknown> {
    const url = new URL(this.#baseUrl + path);
    for (const [name, value] of Object.entries(options.query ?? {})) {
      if (value !== undefined && value !== null) url.searchParams.set(name, String(value));
    }

    for (let attempt = 0; ; attempt++) {
      const headers: Record<string, string> = { Authorization: `Bearer ${await this.#bearer(attempt > 0)}`, Accept: "application/json" };
      if (options.body !== undefined) headers["Content-Type"] = "application/json";
      if (options.idempotencyKey !== undefined) headers["Idempotency-Key"] = options.idempotencyKey;
      const response = await this.#fetch(url, { method, headers, body: options.body === undefined ? undefined : JSON.stringify(options.body) });

      if (response.status === 429 && attempt < maxRetries) {
        await sleep(Number(response.headers.get("Retry-After") ?? "1") * 1000);
        continue;
      }
      if (response.status === 401 && "clientId" in this.#credentials && attempt === 0) {
        this.#token = undefined;
        continue;
      }
      if (!response.ok) throw new AskwerkError(response.status, await problemOf(response));
      if (options.binary) return new Uint8Array(await response.arrayBuffer());
      const text = await response.text();
      return text === "" ? undefined : JSON.parse(text);
    }
  }

  async *byCursor<T>(page: (cursor: string | undefined) => Promise<{ items: T[]; nextCursor?: string | null }>): AsyncIterable<T> {
    let cursor: string | undefined;
    do {
      const next = await page(cursor);
      yield* next.items;
      cursor = next.nextCursor ?? undefined;
    } while (cursor !== undefined);
  }

  async *bySequence<T>(since: number, page: (since: number) => Promise<{ items: T[]; nextSince: number }>): AsyncIterable<T> {
    for (;;) {
      const next = await page(since);
      if (next.items.length === 0) return;
      yield* next.items;
      since = next.nextSince;
    }
  }

  async #bearer(renew: boolean): Promise<string> {
    const credentials = this.#credentials;
    if ("apiKey" in credentials) return credentials.apiKey;
    if (!renew && this.#token !== undefined && this.#token.until > Date.now()) return this.#token.value;

    const form = new URLSearchParams({ grant_type: "client_credentials", client_id: credentials.clientId, client_secret: credentials.clientSecret });
    if (credentials.scope !== undefined) form.set("scope", credentials.scope);
    const response = await this.#fetch(`${this.#baseUrl}/api/v1/oauth/token`, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: form });
    if (!response.ok) throw new AskwerkError(response.status, await problemOf(response));
    const token = (await response.json()) as { access_token: string; expires_in: number };
    this.#token = { value: token.access_token, until: Date.now() + (token.expires_in - 60) * 1000 };
    return token.access_token;
  }
}

async function problemOf(response: Response) {
  try {
    return (await response.json()) as Record<string, string>;
  } catch {
    return {};
  }
}

export const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
