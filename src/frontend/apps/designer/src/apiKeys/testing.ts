// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written ApiKeysClient for the API page's tests.
import { ApiError, type ApiKeyItem, type ApiKeysClient, type NewApiKey } from "@askwerk/app-client";

export const sampleKeys: ApiKeyItem[] = [
  { id: "k1", name: "Reporting server", scopes: ["surveys:read", "responses:read"], shown: "awk_7Hq2…", createdAt: "2026-10-01T08:00:00Z", lastUsedAt: "2026-10-14T06:00:00Z", expiresAt: "2026-12-30T08:00:00Z" },
  { id: "k2", name: "HR system sync", scopes: ["surveys:read", "surveys:write", "responses:read", "responses:write", "participants:read", "participants:write", "contacts:read", "contacts:write", "webhooks:manage"], shown: "awk_Lm9x…", createdAt: "2026-09-14T08:00:00Z", lastUsedAt: null, expiresAt: null },
  { id: "k3", name: "Old intranet", scopes: ["surveys:read"], shown: "awk_c41T…", createdAt: "2026-03-02T08:00:00Z", lastUsedAt: "2026-04-03T08:00:00Z", expiresAt: "2026-04-30T08:00:00Z" },
];

export class FakeApiKeysClient implements ApiKeysClient {
  readonly calls: [string, ...unknown[]][] = [];
  keys: ApiKeyItem[] = [...sampleKeys];
  refuse = false;

  list = () => {
    this.calls.push(["list"]);
    return this.refuse ? Promise.reject(new ApiError(403, undefined, "forbidden", { code: "role-not-allowed" })) : Promise.resolve([...this.keys]);
  };

  create = (key: NewApiKey) => {
    this.calls.push(["create", key]);

    if (key.name.trim() === "") {
      return Promise.reject(new ApiError(400, undefined, "invalid", { errors: { name: ["name-required"] } }));
    }

    const item: ApiKeyItem = { id: "k9", name: key.name, scopes: [...key.scopes], shown: "awk_Zz00…", createdAt: "2026-10-15T08:00:00Z", lastUsedAt: null, expiresAt: null };
    this.keys = [item, ...this.keys];

    return Promise.resolve({ key: item, secret: "awk_Zz00secretsecretsecretsecretsecretsecret0000" });
  };

  revoke = (keyId: string) => {
    this.calls.push(["revoke", keyId]);
    this.keys = this.keys.filter((key) => key.id !== keyId);
    return Promise.resolve();
  };
}
