// SPDX-License-Identifier: Apache-2.0
// The Askwerk Public API for TypeScript and JavaScript: no dependencies, fetch only (Node 20+, browsers, Deno, Bun).
import { ExportsApi, ParticipantsApi, ResponsesApi, SurveysApi, UsersApi, WebhooksApi, type PublicExport } from "./generated.ts";
import { AskwerkError, sleep, Transport, type Credentials } from "./transport.ts";

export * from "./generated.ts";
export { AskwerkError, type Credentials } from "./transport.ts";

/** Options of an Askwerk client: the address and either an API key or an OAuth client. */
export type AskwerkOptions = { baseUrl: string; fetch?: typeof fetch } & Credentials;

/** Exports, with waiting until a file is ready. */
export class Exports extends ExportsApi {
  /** Polls the export until it is ready; throws when it failed or expired, or after the timeout. */
  async waitUntilReady(exportId: string, { intervalMs = 2000, timeoutMs = 600_000 } = {}): Promise<PublicExport> {
    const until = Date.now() + timeoutMs;
    for (;;) {
      const current = await this.get(exportId);
      if (current.status === "ready") return current;
      if (current.status === "failed" || current.status === "expired") throw new AskwerkError(409, { type: current.error?.type ?? current.status, title: current.error?.title ?? `The export ${current.status}.`, detail: current.error?.detail ?? undefined });
      if (Date.now() > until) throw new AskwerkError(408, { title: "The export was not ready in time." });
      await sleep(intervalMs);
    }
  }
}

/** A client of the Askwerk Public API. */
export class Askwerk {
  readonly surveys: SurveysApi;
  readonly responses: ResponsesApi;
  readonly exports: Exports;
  readonly webhooks: WebhooksApi;
  readonly users: UsersApi;
  readonly participants: ParticipantsApi;

  constructor(options: AskwerkOptions) {
    const { baseUrl, fetch: fetcher, ...credentials } = options;
    const transport = new Transport(baseUrl, credentials as Credentials, fetcher);
    this.surveys = new SurveysApi(transport);
    this.responses = new ResponsesApi(transport);
    this.exports = new Exports(transport);
    this.webhooks = new WebhooksApi(transport);
    this.users = new UsersApi(transport);
    this.participants = new ParticipantsApi(transport);
  }
}

/**
 * Checks a webhook delivery's Askwerk-Signature header: HMAC-SHA256 of "{t}.{body}" with the webhook's secret, at most
 * toleranceSeconds old. Pass the raw request body, not parsed JSON.
 */
export async function verifyWebhook(secret: string, header: string, body: string, toleranceSeconds = 300): Promise<boolean> {
  const parts = Object.fromEntries(header.split(",").map((part) => part.split("=", 2) as [string, string]));
  const t = Number(parts.t);
  if (!Number.isFinite(t) || Math.abs(Date.now() / 1000 - t) > toleranceSeconds || parts.v1 === undefined) return false;
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(`${parts.t}.${body}`)));
  const expected = Array.from(mac, (byte) => byte.toString(16).padStart(2, "0")).join("");
  let difference = expected.length ^ parts.v1.length;
  for (let index = 0; index < expected.length; index++) difference |= expected.charCodeAt(index) ^ (parts.v1.charCodeAt(index) || 0);
  return difference === 0;
}
