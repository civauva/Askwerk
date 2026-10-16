// SPDX-License-Identifier: AGPL-3.0-only
import type { OAuthClientItem, OAuthClientsClient } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiKeysPage } from "./ApiKeysPage";
import { FakeApiKeysClient } from "./testing";

afterEach(cleanup);

const hr: OAuthClientItem = { id: "c1", name: "HR system", clientId: "cl_Abc123", scopes: ["surveys:read", "responses:read"], createdAt: "2026-10-01T08:00:00Z", lastUsedAt: null, expiresAt: null };

class FakeClients implements OAuthClientsClient {
  readonly calls: unknown[][] = [];
  list = () => (this.calls.push(["list"]), Promise.resolve([hr]));
  create = (client: { name: string; scopes: readonly string[]; expiresInDays: 30 | 90 | 365 | null }) =>
    (this.calls.push(["create", client]), Promise.resolve({ client: { ...hr, id: "c9", name: client.name, clientId: "cl_New999" }, clientSecret: "acs_secret" }));
  revoke = (id: string) => (this.calls.push(["revoke", id]), Promise.resolve());
}

async function shown(clients = new FakeClients()) {
  const writeClipboard = vi.fn(() => Promise.resolve());
  const view = render(<ApiKeysPage client={new FakeApiKeysClient()} oauthClients={clients} locale="en" writeClipboard={writeClipboard} timeZone="UTC" now={() => new Date("2026-10-15T10:00:00Z")} />);
  await screen.findByRole("heading", { level: 1 });
  fireEvent.click(screen.getByRole("tab", { name: "OAuth clients" }));
  await screen.findByText("cl_Abc123");
  await act(async () => {});

  return { clients, writeClipboard, ...view };
}

describe("OAuth clients on the API page (AWK-32, C32 B)", () => {
  it("lists the clients with their id and access", async () => {
    const { container } = await shown();

    const row = within(screen.getByRole("row", { name: /HR system/ }));
    expect(row.getByText("Read results")).toBeTruthy();
    expect(row.getAllByText("never")).toHaveLength(2);
    await expectNoAxeViolations(container);
  });

  it("creates a client and shows its id and secret once", async () => {
    const { clients, writeClipboard } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Create client" }));
    const create = within(screen.getByRole("dialog", { name: "Create OAuth client" }));
    fireEvent.change(create.getByRole("textbox", { name: "Name" }), { target: { value: "CRM" } });
    fireEvent.click(create.getByRole("radio", { name: /^Full access/ }));
    fireEvent.click(create.getByRole("button", { name: "Create" }));

    expect(await create.findByText("cl_New999")).toBeTruthy();
    expect(create.getByText("acs_secret")).toBeTruthy();
    expect(clients.calls).toContainEqual(["create", { name: "CRM", scopes: expect.arrayContaining(["webhooks:manage"]), expiresInDays: 365 }]);
    fireEvent.click(create.getByRole("button", { name: "Copy the secret" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith("acs_secret"));
  });

  it("revokes a client after asking", async () => {
    const { clients } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Revoke HR system" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Revoke OAuth client" })).getByRole("button", { name: "Revoke" }));

    await waitFor(() => expect(clients.calls).toContainEqual(["revoke", "c1"]));
  });
});
