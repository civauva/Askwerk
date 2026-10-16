// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** Where this Askwerk's public API answers (AWK-34). */
export type DevelopersInfo = { baseUrl: string; apiUrl: string };

/** A trial key for Try it: read-only, one hour; the secret is in this answer only (AWK-34). */
export type TrialKey = { key: { id: string; scopes: string[]; expiresAt: string | null; shown: string }; secret: string };

/** The Developers hub (AWK-34, W27): the API's address, a trial key, downloads, and the public API's description. */
export interface DevelopersClient {
  info(): Promise<DevelopersInfo>;
  /** 403 role-not-allowed for roles other than owner and admin. */
  trialKey(): Promise<TrialKey>;
  /** The public API's OpenAPI description. */
  openApi(): Promise<unknown>;
  /** The Postman collection with this Askwerk's address, as a download link. */
  readonly postmanUrl: string;
  readonly openApiUrl: string;
  readonly exampleUrl: string;
}

export function createDevelopersClient(options: AppClientOptions): DevelopersClient {
  const request = createRequest(options);

  return {
    info: () => request<DevelopersInfo>("GET", "/api/app/developers"),
    trialKey: () => request<TrialKey>("POST", "/api/app/api-keys/trial"),
    openApi: () => request<unknown>("GET", "/api/v1/openapi.json"),
    postmanUrl: "/api/app/developers/postman",
    openApiUrl: "/api/v1/openapi.json",
    exampleUrl: "/api/app/developers/examples/sql-server-sync",
  };
}
