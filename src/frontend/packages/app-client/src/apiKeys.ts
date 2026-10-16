// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** A key of the organisation for the Public API, without its secret. */
export interface ApiKeyItem {
  readonly id: string;
  readonly name: string;
  readonly scopes: readonly string[];
  /** The key's first eight characters, e.g. awk_7Hq2…. */
  readonly shown: string;
  readonly createdAt: string;
  readonly lastUsedAt: string | null;
  /** Null for a key that never expires. */
  readonly expiresAt: string | null;
}

/** A new key: a preset's scopes or some of them; 30, 90 or 365 days, or null for never. */
export interface NewApiKey {
  readonly name: string;
  readonly scopes: readonly string[];
  readonly expiresInDays: 30 | 90 | 365 | null;
}

/** The presets of the create dialog (W14 D). */
export const apiKeyPresets = {
  readResults: ["surveys:read", "responses:read"],
  fullAccess: ["surveys:read", "surveys:write", "responses:read", "responses:write", "participants:read", "participants:write", "webhooks:manage"],
} as const;

/** Every scope a key may carry: Full access and, never in a preset, users:manage for provisioning (AWK-12). */
export const apiKeyScopes = [...apiKeyPresets.fullAccess, "users:manage"] as const;

/** Settings → API (AWK-25, W14 C and D): owners and admins only; failures are ApiError (403 role-not-allowed). */
export interface ApiKeysClient {
  list(): Promise<ApiKeyItem[]>;
  /** The secret is in this answer only. */
  create(key: NewApiKey): Promise<{ key: ApiKeyItem; secret: string }>;
  revoke(keyId: string): Promise<void>;
}

export function createApiKeysClient(options: AppClientOptions): ApiKeysClient {
  const request = createRequest(options);

  return {
    list: () => request<{ items: ApiKeyItem[] }>("GET", "/api/app/api-keys").then((answer) => answer.items),
    create: (key) => request<{ key: ApiKeyItem; secret: string }>("POST", "/api/app/api-keys", key),
    revoke: (keyId) => request("DELETE", `/api/app/api-keys/${encodeURIComponent(keyId)}`),
  };
}
