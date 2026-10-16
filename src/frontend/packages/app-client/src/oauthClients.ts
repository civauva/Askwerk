// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** An OAuth client of the organisation (AWK-32, C32 B), never with its secret. */
export interface OAuthClientItem {
  readonly id: string;
  readonly name: string;
  /** The public client id (cl_…). */
  readonly clientId: string;
  readonly scopes: readonly string[];
  readonly createdAt: string;
  readonly lastUsedAt: string | null;
  readonly expiresAt: string | null;
}

/** Organisation → API → OAuth clients: owners and admins only; failures are ApiError (403 role-not-allowed). */
export interface OAuthClientsClient {
  list(): Promise<OAuthClientItem[]>;
  /** The secret is in this answer only. */
  create(client: { name: string; scopes: readonly string[]; expiresInDays: 30 | 90 | 365 | null }): Promise<{ client: OAuthClientItem; clientSecret: string }>;
  revoke(id: string): Promise<void>;
}

export function createOAuthClientsClient(options: AppClientOptions): OAuthClientsClient {
  const request = createRequest(options);

  return {
    list: () => request<{ items: OAuthClientItem[] }>("GET", "/api/app/oauth-clients").then((answer) => answer.items),
    create: (client) => request<{ client: OAuthClientItem; clientSecret: string }>("POST", "/api/app/oauth-clients", client),
    revoke: (id) => request("DELETE", `/api/app/oauth-clients/${encodeURIComponent(id)}`),
  };
}
