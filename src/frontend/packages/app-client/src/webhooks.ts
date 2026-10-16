// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** The events a webhook can get (AWK-32, C32 A). */
export const webhookEvents = ["response.completed", "export.completed", "survey.published", "survey.closed"] as const;

export type WebhookEvent = (typeof webhookEvents)[number];

/** A webhook of the organisation, never with its secret. */
export interface WebhookItem {
  readonly id: string;
  readonly url: string;
  readonly events: readonly string[];
  readonly description: string | null;
  readonly active: boolean;
  /** Paused for failing 3 days; change active to true to resume. */
  readonly paused: boolean;
  readonly createdAt: string;
  readonly lastDelivery: { readonly status: "pending" | "delivered" | "failed"; readonly at: string; readonly responseStatus: number | null } | null;
}

export interface WebhookDeliveryItem {
  readonly id: string;
  readonly event: string;
  readonly eventId: string;
  readonly status: "pending" | "delivered" | "failed";
  readonly attempts: number;
  readonly responseStatus: number | null;
  readonly error: string | null;
  readonly createdAt: string;
  readonly deliveredAt: string | null;
}

/** Organisation → API → Webhooks (AWK-32): owners and admins only; failures are ApiError (403 role-not-allowed, 400 url-invalid…). */
export interface WebhooksClient {
  list(): Promise<WebhookItem[]>;
  /** The secret is in this answer only. */
  create(webhook: { url: string; events: readonly string[]; description?: string }): Promise<{ webhook: WebhookItem; secret: string }>;
  change(webhookId: string, change: { url?: string; events?: readonly string[]; active?: boolean }): Promise<WebhookItem>;
  remove(webhookId: string): Promise<void>;
  deliveries(webhookId: string): Promise<WebhookDeliveryItem[]>;
  replay(webhookId: string, deliveryId: string): Promise<void>;
  ping(webhookId: string): Promise<void>;
}

export function createWebhooksClient(options: AppClientOptions): WebhooksClient {
  const request = createRequest(options);
  const at = (webhookId: string) => `/api/app/webhooks/${encodeURIComponent(webhookId)}`;

  return {
    list: () => request<{ items: WebhookItem[] }>("GET", "/api/app/webhooks").then((answer) => answer.items),
    create: (webhook) => request<{ webhook: WebhookItem; secret: string }>("POST", "/api/app/webhooks", webhook),
    change: (webhookId, change) => request<WebhookItem>("PATCH", at(webhookId), change),
    remove: (webhookId) => request("DELETE", at(webhookId)),
    deliveries: (webhookId) => request<{ items: WebhookDeliveryItem[] }>("GET", `${at(webhookId)}/deliveries`).then((answer) => answer.items),
    replay: (webhookId, deliveryId) => request("POST", `${at(webhookId)}/deliveries/${encodeURIComponent(deliveryId)}/replay`),
    ping: (webhookId) => request("POST", `${at(webhookId)}/ping`),
  };
}
