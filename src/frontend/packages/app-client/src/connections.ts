// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** What a connection is for (W36). */
export const connectionGoals = ["answers", "people", "sign-in", "embed"] as const;

export type ConnectionGoal = (typeof connectionGoals)[number];

/** What a connection's system may do, in words instead of scopes. */
export const connectionPermissions = ["people", "results", "surveys"] as const;

export type ConnectionPermission = (typeof connectionPermissions)[number];

export type ConnectionHealth = "works" | "waiting" | "warning" | "failing" | "paused";

export type ConnectionReason = "credentials-ending" | "credentials-ended" | "deliveries-failed" | "deliveries-failing" | "webhook-paused";

/** How the system calls Askwerk. */
export interface ConnectionCalls {
  readonly signIn: "oauth" | "api-key";
  /** The client id, or the key's first characters. */
  readonly shown: string;
  readonly permissions: readonly ConnectionPermission[];
  readonly lastCallAt: string | null;
  readonly expiresAt: string | null;
  readonly revoked: boolean;
}

/** How Askwerk calls the system. */
export interface ConnectionWebhook {
  readonly url: string;
  readonly events: readonly string[];
  /** Empty: all surveys, also new ones. */
  readonly surveys: readonly { readonly id: string; readonly title: string }[];
  readonly deliveredThisWeek: number;
  readonly failedThisWeek: number;
  readonly lastDeliveredAt: string | null;
  readonly failingSince: string | null;
}

/** A connection as Connections shows it. */
export interface ConnectionItem {
  readonly id: string;
  readonly name: string;
  readonly goals: readonly ConnectionGoal[];
  readonly matchBy: "external-id" | "email";
  readonly health: ConnectionHealth;
  readonly reason: ConnectionReason | null;
  readonly lastActivityAt: string | null;
  readonly pausedAt: string | null;
  readonly pausedBy: string | null;
  readonly calls: ConnectionCalls | null;
  readonly webhook: ConnectionWebhook | null;
  readonly createdAt: string;
}

/** The secrets of a connection, shown once; null where it has none. */
export interface ConnectionCredentials {
  readonly clientId: string | null;
  readonly clientSecret: string | null;
  readonly apiKey: string | null;
  readonly webhookSecret: string | null;
}

/** What Connect a system collected. */
export interface ConnectionRequest {
  name: string;
  goals: readonly ConnectionGoal[];
  /** Where answers go; without it the system reads them. */
  webhookUrl?: string;
  events?: readonly string[];
  /** Empty: all surveys. */
  surveyIds?: readonly string[];
  signIn?: "oauth" | "api-key";
  permissions?: readonly ConnectionPermission[];
  matchBy?: "external-id" | "email";
}

/** Administration → Connections (W36): owners and admins only; failures are ApiError (403 role-not-allowed, 400 at the field…). */
export interface ConnectionsClient {
  list(): Promise<ConnectionItem[]>;
  get(id: string): Promise<ConnectionItem>;
  /** The secrets are in this answer only. */
  create(request: ConnectionRequest): Promise<{ connection: ConnectionItem; credentials: ConnectionCredentials }>;
  change(id: string, change: { name?: string; matchBy?: "external-id" | "email"; webhookUrl?: string; events?: readonly string[]; surveyIds?: readonly string[] }): Promise<ConnectionItem>;
  /** Sends a test event to its webhook. */
  test(id: string): Promise<void>;
  /** New secrets; the old ones stop at once. */
  newSecrets(id: string): Promise<ConnectionCredentials>;
  pause(id: string, paused: boolean): Promise<ConnectionItem>;
  remove(id: string): Promise<void>;
}

export function createConnectionsClient(options: AppClientOptions): ConnectionsClient {
  const request = createRequest(options);
  const at = (id: string) => `/api/app/connections/${encodeURIComponent(id)}`;

  return {
    list: () => request<{ items: ConnectionItem[] }>("GET", "/api/app/connections").then((answer) => answer.items),
    get: (id) => request<ConnectionItem>("GET", at(id)),
    create: (body) => request<{ connection: ConnectionItem; credentials: ConnectionCredentials }>("POST", "/api/app/connections", body),
    change: (id, change) => request<ConnectionItem>("PATCH", at(id), change),
    test: (id) => request("POST", `${at(id)}/test`),
    newSecrets: (id) => request<ConnectionCredentials>("POST", `${at(id)}/secrets`),
    pause: (id, paused) => request<ConnectionItem>("PUT", `${at(id)}/paused`, { paused }),
    remove: (id) => request("DELETE", at(id)),
  };
}
