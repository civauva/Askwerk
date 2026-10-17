// SPDX-License-Identifier: AGPL-3.0-only
import type { ConnectionCredentials, ConnectionItem, ConnectionRequest, ConnectionsClient, SurveyList } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ConnectionsPage } from "./ConnectionsPage";
import { sampleOf } from "./samples";

afterEach(cleanup);

const crm: ConnectionItem = {
  id: "c1",
  name: "Acme CRM",
  goals: ["answers", "people"],
  matchBy: "external-id",
  health: "works",
  reason: null,
  lastActivityAt: "2026-10-17T09:56:00Z",
  pausedAt: null,
  pausedBy: null,
  calls: { signIn: "oauth", shown: "cl_7Hq2", permissions: ["people"], lastCallAt: "2026-10-17T09:56:00Z", expiresAt: null, revoked: false },
  webhook: { url: "https://crm.acme.at/hooks/askwerk", events: ["response.completed"], surveys: [], deliveredThisWeek: 412, failedThisWeek: 0, lastDeliveredAt: "2026-10-17T09:56:00Z", failingSince: null },
  createdAt: "2026-10-01T08:00:00Z",
};
const nightly: ConnectionItem = { ...crm, id: "c2", name: "Power BI nightly", goals: ["answers"], health: "warning", reason: "credentials-ending", webhook: null, calls: { signIn: "api-key", shown: "awk_AbCd…", permissions: ["results"], lastCallAt: null, expiresAt: "2026-10-23T00:00:00Z", revoked: false } };
const portal: ConnectionItem = { ...crm, id: "c3", name: "Customer portal", goals: ["embed"], health: "paused", pausedAt: "2026-10-10T08:00:00Z", pausedBy: "Anna Berger", calls: null, webhook: null };
const secrets: ConnectionCredentials = { clientId: "cl_7Hq2x9Fm", clientSecret: "acs_secret", apiKey: null, webhookSecret: "whsec_secret" };

class FakeConnections implements ConnectionsClient {
  readonly calls: unknown[][] = [];
  items: ConnectionItem[] = [crm, nightly, portal];
  current: ConnectionItem = { ...crm, health: "waiting", lastActivityAt: null, calls: { ...crm.calls!, lastCallAt: null }, webhook: { ...crm.webhook!, deliveredThisWeek: 0, lastDeliveredAt: null } };
  list = () => (this.calls.push(["list"]), Promise.resolve([...this.items]));
  get = (id: string) => (this.calls.push(["get", id]), Promise.resolve(this.current));
  create = (request: ConnectionRequest) => (this.calls.push(["create", request]), Promise.resolve({ connection: this.current, credentials: secrets }));
  change = (id: string, change: object) => (this.calls.push(["change", id, change]), Promise.resolve({ ...crm, ...change }));
  test = (id: string) => (this.calls.push(["test", id]), Promise.resolve());
  newSecrets = (id: string) => (this.calls.push(["newSecrets", id]), Promise.resolve({ ...secrets, clientSecret: "acs_new" }));
  pause = (id: string, paused: boolean) => (this.calls.push(["pause", id, paused]), Promise.resolve({ ...crm, health: paused ? ("paused" as const) : ("works" as const), pausedAt: paused ? "2026-10-17T10:00:00Z" : null, pausedBy: paused ? "Dana" : null }));
  remove = (id: string) => (this.calls.push(["remove", id]), Promise.resolve());
}

const surveyList = { items: [{ id: "s1", title: "Customer satisfaction 2026" }, { id: "s2", title: "Patient experience" }], counts: { all: 2, draft: 0, published: 2, closed: 0 }, total: 2 } as unknown as SurveyList;

async function shown(client = new FakeConnections()) {
  const writeClipboard = vi.fn(() => Promise.resolve());
  const onNavigate = vi.fn();
  const view = render(<ConnectionsPage client={client} surveys={{ list: () => Promise.resolve(surveyList) }} locale="en" onNavigate={onNavigate} writeClipboard={writeClipboard} baseUrl="https://surveys.acme.at" now={() => new Date("2026-10-17T10:00:00Z")} pollMs={20} />);
  await screen.findByRole("list", { name: "Connections" });

  return { client, writeClipboard, onNavigate, ...view };
}

const item = (name: string) => within(screen.getByRole("heading", { level: 2, name: new RegExp(name) }).closest("li")!);

describe("Connections (W36)", () => {
  it("lists each system with what it does in both directions and whether it works", async () => {
    const { container } = await shown();

    expect(item("Acme CRM").getByText("Works")).toBeTruthy();
    expect(item("Acme CRM").getByText("delivered this week: 412 · last 4 minutes ago")).toBeTruthy();
    expect(item("Acme CRM").getByText(/response\.completed →/)).toBeTruthy();
    expect(item("Acme CRM").getByText(/OAuth client · people/)).toBeTruthy();
    expect(item("Power BI nightly").getByText("the key ends on Oct 23")).toBeTruthy();
    expect(item("Customer portal").getByText("since Oct 10 by Anna Berger")).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("connects a CRM in three steps, presets permissions from the goals and shows the secrets once", async () => {
    const { client, writeClipboard } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "+ Connect a system" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Connect a system" }));
    expect((dialog.getByRole("button", { name: "Next" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(dialog.getByRole("checkbox", { name: /Get answers into my system/ }));
    fireEvent.click(dialog.getByRole("checkbox", { name: /Send people from my system/ }));
    fireEvent.click(dialog.getByRole("button", { name: "Next" }));

    fireEvent.click(dialog.getByRole("button", { name: "Create the connection" }));
    expect(dialog.getByText("Give it a name.")).toBeTruthy();
    fireEvent.change(dialog.getByRole("textbox", { name: /Name/ }), { target: { value: "Acme CRM" } });
    fireEvent.change(dialog.getByRole("textbox", { name: /Answers go to/ }), { target: { value: "https://crm.acme.at/hooks/askwerk" } });
    fireEvent.click(dialog.getByRole("radio", { name: "Only these" }));
    fireEvent.click(await dialog.findByRole("checkbox", { name: "Customer satisfaction 2026" }));
    expect((dialog.getByRole("checkbox", { name: /Add and change people/ }) as HTMLInputElement).checked).toBe(true);
    expect((dialog.getByRole("checkbox", { name: /Read surveys and answers/ }) as HTMLInputElement).checked).toBe(false);
    expect((dialog.getByRole("radio", { name: "Client ID and secret (OAuth)" }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(dialog.getByRole("button", { name: "Create the connection" }));

    await dialog.findByRole("heading", { name: "Credentials · shown once" });
    expect(client.calls).toContainEqual(["create", { name: "Acme CRM", goals: ["answers", "people"], webhookUrl: "https://crm.acme.at/hooks/askwerk", events: ["response.completed"], surveyIds: ["s1"], signIn: "oauth", permissions: ["people"], matchBy: "external-id" }]);
    expect(dialog.getByLabelText("Client ID").textContent).toBe("cl_7Hq2x9Fm");
    expect(dialog.getByLabelText("Client secret").textContent).not.toContain("acs_secret");
    fireEvent.click(dialog.getByRole("button", { name: "Copy: Client secret" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith("acs_secret"));
    expect(dialog.getByRole("tabpanel").textContent).toContain('AskwerkAuth.ClientCredentials("cl_7Hq2x9Fm"');
    expect(dialog.getByRole("tabpanel").textContent).not.toContain("acs_secret");
    expect(dialog.getByText(/Waiting for the first call/)).toBeTruthy();
  });

  it("sends a test event and turns green when it is delivered and the system calls", async () => {
    const { client } = await shown();
    fireEvent.click(screen.getByRole("button", { name: "+ Connect a system" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Connect a system" }));
    fireEvent.click(dialog.getByRole("checkbox", { name: /Get answers into my system/ }));
    fireEvent.click(dialog.getByRole("checkbox", { name: /Send people from my system/ }));
    fireEvent.click(dialog.getByRole("button", { name: "Next" }));
    fireEvent.change(dialog.getByRole("textbox", { name: /Name/ }), { target: { value: "Acme CRM" } });
    fireEvent.change(dialog.getByRole("textbox", { name: /Answers go to/ }), { target: { value: "https://crm.acme.at/hooks/askwerk" } });
    fireEvent.click(dialog.getByRole("button", { name: "Create the connection" }));
    await dialog.findByRole("button", { name: "Send a test event" });

    fireEvent.click(dialog.getByRole("button", { name: "Send a test event" }));
    await dialog.findByText("Sent; waiting for crm.acme.at to answer…");
    client.current = { ...client.current, webhook: { ...client.current.webhook!, lastDeliveredAt: new Date(Date.now() + 5000).toISOString() }, calls: { ...client.current.calls!, lastCallAt: new Date().toISOString() } };

    await dialog.findByText("Test event delivered to crm.acme.at, signed with the webhook secret below.");
    await dialog.findByText(/First call seen/);
    expect(client.calls).toContainEqual(["test", "c1"]);
  });

  it("asks for a key and read results when the system fetches answers itself, and points sign-in to its page", async () => {
    const { client, onNavigate } = await shown();
    fireEvent.click(screen.getByRole("button", { name: "+ Connect a system" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Connect a system" }));
    fireEvent.click(dialog.getByRole("checkbox", { name: /Get answers into my system/ }));
    fireEvent.click(dialog.getByRole("checkbox", { name: /Sign in with our login/ }));
    fireEvent.click(dialog.getByRole("button", { name: "Next" }));

    expect((dialog.getByRole("radio", { name: "API key" }) as HTMLInputElement).checked).toBe(true);
    expect((dialog.getByRole("checkbox", { name: /Read surveys and answers/ }) as HTMLInputElement).checked).toBe(true);
    expect(dialog.queryByRole("radio", { name: "Email only" })).toBeNull();
    fireEvent.click(dialog.getByRole("button", { name: "Open Sign-in" }));
    expect(onNavigate).toHaveBeenCalledWith("/sign-in-settings");
    fireEvent.change(dialog.getByRole("textbox", { name: /Name/ }), { target: { value: "Power BI nightly" } });
    fireEvent.click(dialog.getByRole("button", { name: "Create the connection" }));

    await waitFor(() => expect(client.calls).toContainEqual(["create", { name: "Power BI nightly", goals: ["answers", "sign-in"], signIn: "api-key", permissions: ["results"] }]));
  });

  it("opens a connection to pause it, make new secrets and remove it", async () => {
    const { client } = await shown();

    fireEvent.click(screen.getByRole("button", { name: "Open: Acme CRM" }));
    const panel = within(await screen.findByRole("dialog", { name: "Acme CRM" }));
    fireEvent.click(panel.getByRole("button", { name: "Pause" }));
    await panel.findByText("Paused");
    expect(client.calls).toContainEqual(["pause", "c1", true]);

    fireEvent.click(panel.getByRole("button", { name: "New secrets" }));
    fireEvent.click(within(await screen.findByRole("dialog", { name: "Make new secrets?" })).getByRole("button", { name: "Make new secrets" }));
    await panel.findByRole("heading", { name: "Credentials · shown once" });

    fireEvent.click(panel.getByRole("button", { name: "Remove" }));
    fireEvent.click(within(await screen.findByRole("dialog", { name: "Remove Acme CRM?" })).getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(client.calls).toContainEqual(["remove", "c1"]));
    await act(async () => {});
    expect(screen.queryByRole("dialog", { name: "Acme CRM" })).toBeNull();
  });
});

describe("Code samples (W36)", () => {
  it("never carries a secret, and uses the SDK of each language", () => {
    const setup = { baseUrl: "https://surveys.acme.at", signIn: "oauth" as const, clientId: "cl_1", people: true, results: false, webhook: true };

    expect(sampleOf("typescript", setup)).toContain("askwerk.participants.invite(surveyId");
    expect(sampleOf("python", setup)).toContain('client_secret=os.environ["ASKWERK_SECRET"]');
    expect(sampleOf("curl", setup)).toContain("/api/v1/surveys/$SURVEY/participants");
    expect(sampleOf("csharp", { ...setup, signIn: "api-key", people: false, results: true })).toContain("Responses.ListAllAsync");
    expect(sampleOf("postman", setup)).toContain("https://surveys.acme.at/api/v1/openapi.json");
  });
});
