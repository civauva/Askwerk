// SPDX-License-Identifier: AGPL-3.0-only
import type { WebhookDeliveryItem, WebhookItem, WebhooksClient } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiKeysPage } from "./ApiKeysPage";
import { FakeApiKeysClient } from "./testing";

afterEach(cleanup);

const hr: WebhookItem = {
  id: "w1",
  url: "https://hr.acme.at/askwerk",
  events: ["response.completed", "export.completed"],
  description: "HR sync",
  active: true,
  paused: false,
  createdAt: "2026-10-01T08:00:00Z",
  lastDelivery: { status: "delivered", at: "2026-10-15T09:01:00Z", responseStatus: 200 },
};
const old: WebhookItem = { ...hr, id: "w2", url: "https://old.acme.at/hook", events: ["survey.published"], description: null, active: false, paused: true, lastDelivery: { status: "failed", at: "2026-10-14T09:00:00Z", responseStatus: 500 } };
const deliveries: WebhookDeliveryItem[] = [
  { id: "d2", event: "response.completed", eventId: "evt_2", status: "delivered", attempts: 1, responseStatus: 200, error: null, createdAt: "2026-10-15T09:01:00Z", deliveredAt: "2026-10-15T09:01:00Z" },
  { id: "d1", event: "export.completed", eventId: "evt_1", status: "delivered", attempts: 3, responseStatus: 200, error: null, createdAt: "2026-10-15T08:40:00Z", deliveredAt: "2026-10-15T08:52:00Z" },
];

class FakeWebhooks implements WebhooksClient {
  readonly calls: unknown[][] = [];
  items: WebhookItem[] = [hr, old];
  list = () => (this.calls.push(["list"]), Promise.resolve([...this.items]));
  create = (webhook: { url: string; events: readonly string[]; description?: string }) => (this.calls.push(["create", webhook]), Promise.resolve({ webhook: { ...hr, id: "w9", url: webhook.url }, secret: "whsec_secret" }));
  change = (id: string, change: object) => (this.calls.push(["change", id, change]), Promise.resolve(hr));
  remove = (id: string) => (this.calls.push(["remove", id]), Promise.resolve());
  deliveries = (id: string) => (this.calls.push(["deliveries", id]), Promise.resolve(deliveries));
  replay = (id: string, deliveryId: string) => (this.calls.push(["replay", id, deliveryId]), Promise.resolve());
  ping = (id: string) => (this.calls.push(["ping", id]), Promise.resolve());
}

async function shown(webhooks = new FakeWebhooks()) {
  const writeClipboard = vi.fn(() => Promise.resolve());
  const view = render(<ApiKeysPage client={new FakeApiKeysClient()} webhooks={webhooks} locale="en" writeClipboard={writeClipboard} timeZone="UTC" now={() => new Date("2026-10-15T10:00:00Z")} />);
  await screen.findByRole("heading", { level: 1 });
  fireEvent.click(screen.getByRole("tab", { name: "Webhooks" }));
  await screen.findByText("https://hr.acme.at/askwerk");
  await act(async () => {});

  return { webhooks, writeClipboard, ...view };
}

const row = (url: string) => within(screen.getByRole("row", { name: new RegExp(url.replaceAll(".", "\\.")) }));

describe("Webhooks on the API page (AWK-32, C32)", () => {
  it("lists the webhooks with their events and last delivery, and resumes a paused one", async () => {
    const { webhooks, container } = await shown();

    expect(row("https://hr.acme.at/askwerk").getByText("response.completed, export.completed")).toBeTruthy();
    expect(row("https://hr.acme.at/askwerk").getByText("delivered · 09:01")).toBeTruthy();
    expect(row("https://old.acme.at/hook").getByText("paused · failing")).toBeTruthy();
    fireEvent.click(row("https://old.acme.at/hook").getByRole("button", { name: "Resume https://old.acme.at/hook" }));

    await waitFor(() => expect(webhooks.calls).toContainEqual(["change", "w2", { active: true }]));
    await expectNoAxeViolations(container);
  });

  it("adds a webhook and shows its secret once", async () => {
    const { webhooks, writeClipboard } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Add webhook…" }));
    const add = within(screen.getByRole("dialog", { name: "Add webhook" }));
    fireEvent.change(add.getByRole("textbox", { name: "URL" }), { target: { value: "https://crm.acme.at/hooks" } });
    expect(add.getAllByRole("checkbox").every((box) => (box as HTMLInputElement).checked)).toBe(true);
    fireEvent.click(add.getByRole("checkbox", { name: "survey.closed" }));
    fireEvent.click(add.getByRole("button", { name: "Add" }));

    expect(await add.findByText("whsec_secret")).toBeTruthy();
    expect(webhooks.calls).toContainEqual(["create", { url: "https://crm.acme.at/hooks", events: ["response.completed", "export.completed", "survey.published"], description: "" }]);
    fireEvent.click(add.getByRole("button", { name: "Copy" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith("whsec_secret"));
  });

  it("shows the deliveries, replays one, sends a test event and deletes after asking", async () => {
    const { webhooks } = await shown();

    fireEvent.click(row("https://hr.acme.at/askwerk").getByRole("button", { name: "Deliveries of https://hr.acme.at/askwerk" }));
    const log = within(await screen.findByRole("region", { name: "Deliveries · https://hr.acme.at/askwerk" }));
    expect(log.getByText("08:40 · 200 · 3 attempts")).toBeTruthy();
    fireEvent.click(log.getAllByRole("button", { name: /^Replay/ })[1]!);
    await waitFor(() => expect(webhooks.calls).toContainEqual(["replay", "w1", "d1"]));
    fireEvent.click(row("https://hr.acme.at/askwerk").getByRole("button", { name: "Send a test event to https://hr.acme.at/askwerk" }));
    await waitFor(() => expect(webhooks.calls).toContainEqual(["ping", "w1"]));
    fireEvent.click(row("https://hr.acme.at/askwerk").getByRole("button", { name: "Delete https://hr.acme.at/askwerk" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Delete webhook" })).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(webhooks.calls).toContainEqual(["remove", "w1"]));
  });
});
