// SPDX-License-Identifier: AGPL-3.0-only
import type { ConnectionCredentials, ConnectionItem, ConnectionRequest, ConnectionsClient, SurveyList } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ConnectionsPage } from "./ConnectionsPage";
import { ConnectPage } from "./ConnectPage";
import { tileOf } from "./parts";
import { sampleOf } from "./samples";

afterEach(cleanup);

const week = (ok: number[], failed: number[] = [0, 0, 0, 0, 0, 0, 0]) => ok.map((count, index) => ({ day: `2026-10-${11 + index}`, ok: count, failed: failed[index]! }));

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
  webhook: { url: "https://crm.acme.at/hooks/askwerk", events: ["response.completed"], surveys: [], deliveredThisWeek: 412, failedThisWeek: 0, lastDeliveredAt: "2026-10-17T09:56:00Z", failingSince: null, lastStatus: 200, lastDurationMs: 182 },
  createdAt: "2026-10-01T08:00:00Z",
  days: week([50, 60, 55, 70, 65, 72, 40]),
  refusedThisWeek: 0,
  lastExport: null,
  login: null,
  sites: [],
};
const hr: ConnectionItem = {
  ...crm,
  id: "c2",
  name: "Personio (HR)",
  goals: ["people", "sign-in"],
  health: "warning",
  reason: "calls-refused",
  refusedThisWeek: 3,
  webhook: null,
  lastActivityAt: "2026-10-17T08:00:00Z",
  calls: { signIn: "api-key", shown: "awk_AbCd…", permissions: ["people"], lastCallAt: "2026-10-17T08:00:00Z", expiresAt: "2026-10-23T00:00:00Z", revoked: false },
  login: { kind: "oidc", host: "login.acme.at" },
  days: week([5, 3, 4, 6, 2, 5, 4], [0, 0, 1, 0, 2, 0, 0]),
};
const bi: ConnectionItem = { ...crm, id: "c3", name: "Power BI nightly", goals: ["answers"], webhook: null, calls: { signIn: "api-key", shown: "awk_Zz…", permissions: ["results"], lastCallAt: "2026-10-17T03:00:00Z", expiresAt: null, revoked: false }, lastExport: { at: "2026-10-17T03:00:00Z", size: 2_400_000 } };
const portal: ConnectionItem = { ...crm, id: "c4", name: "Customer portal", goals: ["embed"], health: "paused", pausedAt: "2026-10-10T08:00:00Z", pausedBy: "Anna Berger", calls: null, webhook: null, sites: ["portal.acme.at"] };
const secrets: ConnectionCredentials = { clientId: "cl_7Hq2x9Fm", clientSecret: "acs_secret", apiKey: null, webhookSecret: "whsec_secret" };

class FakeConnections implements ConnectionsClient {
  readonly calls: unknown[][] = [];
  items: ConnectionItem[] = [crm, hr, bi, portal];
  current: ConnectionItem = { ...crm, health: "waiting", lastActivityAt: null, calls: { ...crm.calls!, lastCallAt: null }, webhook: { ...crm.webhook!, deliveredThisWeek: 0, lastDeliveredAt: null, lastStatus: null, lastDurationMs: null } };
  list = () => (this.calls.push(["list"]), Promise.resolve([...this.items]));
  get = (id: string) => (this.calls.push(["get", id]), Promise.resolve(this.current));
  create = (request: ConnectionRequest) => (this.calls.push(["create", request]), Promise.resolve({ connection: this.current, credentials: secrets }));
  change = (id: string, change: object) => (this.calls.push(["change", id, change]), Promise.resolve({ ...crm, ...change }));
  test = (id: string) => (this.calls.push(["test", id]), Promise.resolve());
  newSecrets = (id: string) => (this.calls.push(["newSecrets", id]), Promise.resolve({ ...secrets, clientSecret: "acs_new" }));
  pause = (id: string, paused: boolean) => (this.calls.push(["pause", id, paused]), Promise.resolve({ ...crm, health: paused ? ("paused" as const) : ("works" as const), pausedAt: paused ? "2026-10-17T10:00:00Z" : null, pausedBy: paused ? "Dana" : null }));
  remove = (id: string) => (this.calls.push(["remove", id]), Promise.resolve());
  adopt = (kind: "api-key" | "oauth-client" | "webhook", id: string) => (this.calls.push(["adopt", kind, id]), Promise.resolve(crm));
}

const surveyList = { items: [{ id: "s1", title: "Customer satisfaction 2026" }, { id: "s2", title: "Patient experience" }], counts: { all: 2, draft: 0, published: 2, closed: 0 }, total: 2 } as unknown as SurveyList;
const now = () => new Date("2026-10-17T10:00:00Z");

async function list(client = new FakeConnections()) {
  const onNavigate = vi.fn();
  const view = render(<ConnectionsPage client={client} locale="en" onNavigate={onNavigate} writeClipboard={() => Promise.resolve()} baseUrl="https://surveys.acme.at" now={now} />);
  await screen.findByRole("list", { name: "Connections" });

  return { client, onNavigate, ...view };
}

function connect(client = new FakeConnections()) {
  const writeClipboard = vi.fn(() => Promise.resolve());
  const onNavigate = vi.fn();
  const view = render(<ConnectPage client={client} surveys={{ list: () => Promise.resolve(surveyList) }} locale="en" onNavigate={onNavigate} writeClipboard={writeClipboard} baseUrl="https://surveys.acme.at" now={now} pollMs={20} />);

  return { client, writeClipboard, onNavigate, ...view };
}

const row = (name: string) => within(screen.getByRole("heading", { level: 2, name }).closest("li")!);

describe("Connections (W36)", () => {
  it("shows each system with its kind, both directions and whether it works, as in the mockup", async () => {
    const { container, onNavigate } = await list();

    expect(row("Acme CRM").getByText("Works")).toBeTruthy();
    expect(row("Acme CRM").getByText("412 deliveries this week · last 4 min ago")).toBeTruthy();
    expect(row("Acme CRM").getByText(/response\.completed → crm\.acme\.at\/hooks\/askwerk/)).toBeTruthy();
    expect(row("Acme CRM").getByText(/OAuth client · participants/)).toBeTruthy();
    expect(row("Acme CRM").getByRole("img", { name: "The last 7 days" }).children).toHaveLength(7);
    expect(row("Personio (HR)").getByText("3 calls refused")).toBeTruthy();
    expect(row("Personio (HR)").getByText("key expires in 6 days · last call 2 h ago")).toBeTruthy();
    expect(row("Personio (HR)").getByText(/OpenID Connect · login\.acme\.at/)).toBeTruthy();
    expect(row("Power BI nightly").getByText(/^last export \d\d:\d\d · 2\.4 MB$/)).toBeTruthy();
    expect(row("Customer portal").getByText("since Oct 10 by Anna Berger")).toBeTruthy();
    expect(row("Customer portal").getByText(/portal\.acme\.at · signed hand-over of who is answering/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "+ Connect a system" }));
    expect(onNavigate).toHaveBeenCalledWith("/connections/new");
    await expectNoAxeViolations(container);
  });

  it("names the kind of system in its tile", () => {
    expect(tileOf(crm).label).toBe("CRM");
    expect(tileOf(hr).label).toBe("HR");
    expect(tileOf(bi).label).toBe("BI");
    expect(tileOf(portal).label).toBe("WEB");
    expect(tileOf({ name: "Nightly sync", goals: ["answers"] }).label).toBe("NS");
  });

  it("connects a CRM in three steps, presets permissions from the goals and shows the secrets once", async () => {
    const { client, writeClipboard, onNavigate } = connect();

    expect((screen.getByRole("button", { name: "Next" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("checkbox", { name: /Get answers into my system/ }));
    fireEvent.click(screen.getByRole("checkbox", { name: /Send people from my system/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    fireEvent.click(screen.getByRole("button", { name: "Create the connection" }));
    expect(screen.getByText("Give it a name.")).toBeTruthy();
    fireEvent.change(screen.getByRole("textbox", { name: /Name/ }), { target: { value: "Acme CRM" } });
    fireEvent.change(screen.getByRole("textbox", { name: /Answers go to/ }), { target: { value: "https://crm.acme.at/hooks/askwerk" } });
    expect((screen.getByRole("checkbox", { name: /When an answer is complete/ }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByRole("radio", { name: /Only these/ }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "Customer satisfaction 2026" }));
    expect((screen.getByRole("checkbox", { name: /Add and change people/ }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole("checkbox", { name: /Read surveys and answers/ }) as HTMLInputElement).checked).toBe(false);
    expect((screen.getByRole("radio", { name: "Client ID and secret (OAuth)" }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Create the connection" }));

    await screen.findByRole("heading", { name: "Credentials · shown once" });
    expect(client.calls).toContainEqual(["create", { name: "Acme CRM", goals: ["answers", "people"], webhookUrl: "https://crm.acme.at/hooks/askwerk", events: ["response.completed"], surveyIds: ["s1"], signIn: "oauth", permissions: ["people"], matchBy: "external-id" }]);
    expect(screen.getByLabelText("Client ID").textContent).toBe("cl_7Hq2x9Fm");
    expect(screen.getByLabelText("Client secret").textContent).not.toContain("acs_secret");
    expect(screen.getByLabelText("Webhook secret").textContent).toMatch(/^whsec_•+$/);
    fireEvent.click(screen.getByRole("button", { name: "Copy: Client secret" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith("acs_secret"));
    expect(screen.getByRole("tabpanel").textContent).toContain('AskwerkAuth.ClientCredentials("cl_7Hq2x9Fm"');
    expect(screen.getByRole("tabpanel").textContent).not.toContain("acs_secret");
    expect(screen.getByText("Waiting for the first call…")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Guide: connecting a CRM ↗" }).getAttribute("href")).toBe("/developers?guide=crm");
    fireEvent.click(screen.getByRole("button", { name: "Done" }));
    expect(onNavigate).toHaveBeenCalledWith("/connections");
  });

  it("sends a test answer and turns green with the answer's status and time, and when the system calls", async () => {
    const { client } = connect();
    fireEvent.click(screen.getByRole("checkbox", { name: /Get answers into my system/ }));
    fireEvent.click(screen.getByRole("checkbox", { name: /Send people from my system/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    fireEvent.change(screen.getByRole("textbox", { name: /Name/ }), { target: { value: "Acme CRM" } });
    fireEvent.change(screen.getByRole("textbox", { name: /Answers go to/ }), { target: { value: "https://crm.acme.at/hooks/askwerk" } });
    fireEvent.click(screen.getByRole("button", { name: "Create the connection" }));
    await screen.findByRole("button", { name: "Send a test answer" });

    fireEvent.click(screen.getByRole("button", { name: "Send a test answer" }));
    await screen.findByText("Sent; waiting for crm.acme.at to answer…");
    client.current = { ...client.current, webhook: { ...client.current.webhook!, lastDeliveredAt: new Date(Date.now() + 5000).toISOString(), lastStatus: 200, lastDurationMs: 182 }, calls: { ...client.current.calls!, lastCallAt: "2026-10-17T09:59:30Z" } };

    await screen.findByText("Test answer delivered");
    expect(screen.getByText("crm.acme.at answered 200 OK in 182 ms · signed with the secret below")).toBeTruthy();
    await screen.findByText("First call seen");
    expect(client.calls).toContainEqual(["test", "c1"]);
  });

  it("asks for a key and read results when the system fetches answers itself, and points sign-in to its page", async () => {
    const { client, onNavigate } = connect();
    fireEvent.click(screen.getByRole("checkbox", { name: /Get answers into my system/ }));
    fireEvent.click(screen.getByRole("checkbox", { name: /Sign in with our login/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect((screen.getByRole("radio", { name: "API key" }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole("checkbox", { name: /Read surveys and answers/ }) as HTMLInputElement).checked).toBe(true);
    expect(screen.queryByRole("radio", { name: "Email only" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Open Sign-in" }));
    expect(onNavigate).toHaveBeenCalledWith("/sign-in-settings");
    fireEvent.change(screen.getByRole("textbox", { name: /Name/ }), { target: { value: "Power BI nightly" } });
    fireEvent.click(screen.getByRole("button", { name: "Create the connection" }));

    await waitFor(() => expect(client.calls).toContainEqual(["create", { name: "Power BI nightly", goals: ["answers", "sign-in"], signIn: "api-key", permissions: ["results"] }]));
  });

  it("opens a connection to pause it, make new secrets and remove it", async () => {
    const { client } = await list();

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
