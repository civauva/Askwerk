// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type InstallationClient, type OrganisationClient } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AdministratorsPage, InstallationPage, OrganisationPage, OrganisationsPage } from "./AdminPages";
import { TemplatesPage } from "./TemplatesPage";

afterEach(cleanup);

const trace = "4bf92f3577b34da6a3ce929d0e0e4736";
const rejected = (error: ApiError) => {
  const answer = Promise.reject(error);
  answer.catch(() => undefined);
  return answer;
};

function organisationClient(role = "owner") {
  const calls: unknown[][] = [];
  const client: OrganisationClient = {
    get: () => Promise.resolve({ id: "t1", name: "Acme GmbH", slug: "acme", createdAt: "2026-10-12T10:00:00Z", members: 12, surveys: 24, role, timeZone: "Europe/Vienna" }),
    change: (name, slug) => (calls.push(["change", name, slug]), slug === "taken" ? rejected(new ApiError(400, trace, "invalid", { errors: { slug: ["slug-taken"] } })) : Promise.resolve()),
    transfer: (userId) => (calls.push(["transfer", userId]), Promise.resolve()),
    remove: () => (calls.push(["remove"]), Promise.resolve()),
  };

  return { client, calls };
}

const members = { list: () => Promise.resolve({ members: [{ id: "u1", name: "Anna Berger", email: "anna@acme.at", role: "owner" as const, twoFactor: true, lastSignInAt: null, you: true }, { id: "u2", name: "Ben Huber", email: "ben@acme.at", role: "editor" as const, twoFactor: false, lastSignInAt: null, you: false }], invitations: [], canManageOwners: true, emailReady: false }) };

describe("Organisation page (AWK-38, W29)", () => {
  it("renames the organisation, explains a taken short name, and shows the time zone", async () => {
    const { client, calls } = organisationClient();
    const { container } = render(<OrganisationPage client={client} members={members} locale="en" onDeleted={vi.fn()} />);
    const name = await screen.findByRole("textbox", { name: "Name" });

    expect(screen.getByText("Acme GmbH · since 12 Oct 2026 · 12 members · 24 surveys")).toBeTruthy();
    expect(screen.getByText("Europe/Vienna")).toBeTruthy();
    fireEvent.change(screen.getByRole("textbox", { name: "Short name" }), { target: { value: "taken" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Another organisation uses this short name.")).toBeTruthy();
    fireEvent.change(screen.getByRole("textbox", { name: "Short name" }), { target: { value: "acme" } });
    fireEvent.change(name, { target: { value: "Acme Holding" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(calls).toContainEqual(["change", "Acme Holding", "acme"]));
    await expectNoAxeViolations(container);
  });

  it("hands ownership to another member and deletes the organisation, for owners only", async () => {
    const { client, calls } = organisationClient();
    const onDeleted = vi.fn();
    render(<OrganisationPage client={client} members={members} locale="en" onDeleted={onDeleted} />);

    fireEvent.click(await screen.findByRole("button", { name: "Transfer…" }));
    const dialog = within(screen.getByRole("dialog", { name: "Transfer ownership" }));
    fireEvent.click(dialog.getByRole("button", { name: /^New owner/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Ben Huber" }));
    fireEvent.click(dialog.getByRole("button", { name: "Transfer" }));
    await waitFor(() => expect(calls).toContainEqual(["transfer", "u2"]));
    fireEvent.click(screen.getByRole("button", { name: "Delete…" }));
    fireEvent.click(within(screen.getByRole("dialog", { name: "Delete organisation" })).getByRole("button", { name: "Delete organisation" }));

    await waitFor(() => expect(onDeleted).toHaveBeenCalled());
    cleanup();
    render(<OrganisationPage client={organisationClient("admin").client} members={members} locale="en" onDeleted={vi.fn()} />);
    await screen.findByRole("textbox", { name: "Name" });
    expect(screen.queryByRole("button", { name: "Delete…" })).toBeNull();
  });
});

function installationClient() {
  const calls: unknown[][] = [];
  const client: InstallationClient = {
    info: () =>
      Promise.resolve({ name: "Acme Surveys", baseAddress: "https://surveys.acme.at", defaultLanguage: "de", timeZone: "Europe/Vienna", emailConfigured: true, version: "1.4.0", smtpHost: "smtp.acme.at", smtpPort: 587, smtpUserName: "surveys@acme.at", smtpPasswordSet: true, smtpSender: "surveys@acme.at", locked: ["baseAddress", "timeZone"] }),
    organisations: () =>
      Promise.resolve({
        items: [
          { id: "t1", name: "Acme GmbH", slug: "acme", owner: "Anna Berger", members: 12, surveys: 24, responses: 1790, createdAt: "2026-10-12T10:00:00Z", status: "active" as const },
          { id: "t2", name: "Test lab", slug: "test", owner: null, members: 0, surveys: 0, responses: 0, createdAt: "2026-10-15T10:00:00Z", status: "suspended" as const },
          { id: "t4", name: "Old lab", slug: "old", owner: "Anna Berger", members: 6, surveys: 4, responses: 0, createdAt: "2026-10-01T10:00:00Z", status: "deleted" as const, removesAt: new Date(Date.now() + 28.5 * 86_400_000).toISOString() },
        ],
        members: 12,
        surveys: 24,
        recentResponses: 3412,
      }),
    createOrganisation: (name, slug, owner) => (calls.push(["create", name, slug, owner]), Promise.resolve({ id: "t3", ownerJoined: false, invitationLink: "https://surveys.acme.at/join/abc", invitationSent: false })),
    suspend: (id) => (calls.push(["suspend", id]), Promise.resolve()),
    resume: (id) => (calls.push(["resume", id]), Promise.resolve()),
    deleteOrganisation: (id) => (calls.push(["delete", id]), Promise.resolve()),
    restore: (id) => (calls.push(["restore", id]), Promise.resolve()),
    removal: () => Promise.resolve({ surveys: 4, responses: 0, members: 6, other: 3 }),
    removeData: (id, slug) => (calls.push(["removeData", id, slug]), Promise.resolve()),
    change: (settings) => (calls.push(["change", settings]), Promise.resolve()),
    testEmail: (email) => (calls.push(["test", email]), Promise.resolve("anna@acme.at")),
    administrators: () => Promise.resolve([{ id: "u1", name: "Anna Berger", email: "anna@acme.at", me: true }]),
    addAdministrator: (email) => (calls.push(["add", email]), email === "nobody@acme.at" ? rejected(new ApiError(404, trace, "user-not-found")) : Promise.resolve()),
    removeAdministrator: (userId) => (calls.push(["remove", userId]), rejected(new ApiError(409, trace, "last-admin"))),
  };

  return { client, calls };
}

describe("This Askwerk (AWK-38, W29)", () => {
  it("lists every organisation with totals, creates one and gives the owner's link, suspends and resumes", async () => {
    const { client, calls } = installationClient();
    const { container } = render(<OrganisationsPage client={client} locale="en" />);
    const grid = within(await screen.findByRole("grid", { name: "Organisations" }));

    expect(grid.getByText("Acme GmbH")).toBeTruthy();
    expect(grid.getByText("Invited")).toBeTruthy();
    expect(screen.getByText("3,412")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "New organisation" }));
    const dialog = within(screen.getByRole("dialog", { name: "New organisation" }));
    fireEvent.change(dialog.getByRole("textbox", { name: "Name" }), { target: { value: "Falke & Co" } });
    fireEvent.change(dialog.getByRole("textbox", { name: "Short name" }), { target: { value: "falke" } });
    fireEvent.change(dialog.getByRole("textbox", { name: /First owner/ }), { target: { value: "clara@falke.at" } });
    fireEvent.click(dialog.getByRole("button", { name: "Create" }));
    expect(await screen.findByText("https://surveys.acme.at/join/abc")).toBeTruthy();
    expect(calls).toContainEqual(["create", "Falke & Co", "falke", "clara@falke.at"]);
    fireEvent.click(grid.getByRole("button", { name: "Resume: Test lab" }));

    await waitFor(() => expect(calls).toContainEqual(["resume", "t2"]));
  });

  it("adds administrators by email, says when there is no such account, and keeps the last", async () => {
    const { client, calls } = installationClient();
    render(<AdministratorsPage client={client} locale="en" />);
    await screen.findByText("anna@acme.at");

    fireEvent.change(screen.getByRole("textbox", { name: "Email of an existing account" }), { target: { value: "nobody@acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    expect(await screen.findByText("No account with this email. They need to join an organisation first.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Remove" }));

    expect(await screen.findByText("The last administrator stays.")).toBeTruthy();
    expect(calls).toContainEqual(["add", "nobody@acme.at"]);
  });

  it("keeps a deleted organisation 30 days: restores it, or removes its data once the short name is typed (AWK-39)", async () => {
    const { client, calls } = installationClient();
    render(<OrganisationsPage client={client} locale="en" />);
    const grid = within(await screen.findByRole("grid", { name: "Organisations" }));

    expect(grid.getByText("Deleted")).toBeTruthy();
    expect(grid.getByText("29 days left")).toBeTruthy();
    fireEvent.click(grid.getByRole("button", { name: "Restore: Old lab" }));
    await waitFor(() => expect(calls).toContainEqual(["restore", "t4"]));
    fireEvent.click(grid.getByRole("button", { name: "Remove data: Old lab" }));
    const dialog = within(screen.getByRole("dialog", { name: "Remove Old lab's data now?" }));
    expect(await dialog.findByText("Members and invitations")).toBeTruthy();
    const remove = dialog.getByRole("button", { name: "Remove data" });
    expect(remove.hasAttribute("disabled")).toBe(true);
    fireEvent.change(dialog.getByRole("textbox", { name: "Type the short name old to confirm" }), { target: { value: "old" } });
    fireEvent.click(remove);

    await waitFor(() => expect(calls).toContainEqual(["removeData", "t4", "old"]));
    expect(await screen.findByText("The data of Old lab is removed.")).toBeTruthy();
  });

  it("saves the installation's settings, tests the email and shows what the configuration locks (AWK-39)", async () => {
    const { client, calls } = installationClient();
    const { container } = render(<InstallationPage client={client} locale="en" />);
    const name = await screen.findByRole("textbox", { name: "Name" });

    expect(screen.getAllByText("Set in configuration")).toHaveLength(2);
    expect(screen.getByDisplayValue("Europe/Vienna").hasAttribute("readonly")).toBe(true);
    expect(screen.getByPlaceholderText("Unchanged")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Send a test email" }));
    expect(await screen.findByText(/Sent to anna@acme\.at at/)).toBeTruthy();
    fireEvent.change(name, { target: { value: "Acme Umfragen" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(calls).toContainEqual(["change", { name: "Acme Umfragen", defaultLanguage: "de", timeZone: "Europe/Vienna", smtpHost: "smtp.acme.at", smtpPort: 587, smtpUserName: "surveys@acme.at", smtpPassword: null, smtpSender: "surveys@acme.at" }]),
    );
    expect(calls).toContainEqual(["test", { host: "smtp.acme.at", port: 587, userName: "surveys@acme.at", password: null, sender: "surveys@acme.at" }]);
  });
});

describe("Templates (AWK-38, W29)", () => {
  it("lists Askwerk's, the organisation's and the samples, starts a survey from one and deletes the organisation's", async () => {
    const calls: unknown[][] = [];
    const client = {
      listTemplates: () =>
        Promise.resolve([
          { id: "askwerk:nps-short", scope: "askwerk" as const, name: null, names: { en: "NPS, short", de: "NPS, kurz" }, questions: 3, languages: ["de", "en"] },
          { id: "7f1c", scope: "organisation" as const, name: "Acme check-in", names: null, questions: 14, languages: ["de"] },
        ]),
      listSamples: () => Promise.resolve([{ id: "cafe", names: { en: "Café visit" }, kind: {}, features: {}, look: "atelier", tokens: {} as never, languages: ["de", "en"], questions: 7, responses: 86, preview: { question: {}, choices: [] } }]),
      create: (survey: unknown) => (calls.push(["create", survey]), Promise.resolve("s9")),
      createSample: (id: string, title: string) => (calls.push(["sample", id, title]), Promise.resolve("s8")),
      deleteTemplate: (id: string) => (calls.push(["delete", id]), Promise.resolve()),
      editTemplate: (id: string) => (calls.push(["edit", id]), Promise.resolve({ id, surveyId: "e1" })),
      copyTemplate: (id: string, language: string) => (calls.push(["copy", id, language]), Promise.resolve({ id: "c1", surveyId: "e2" })),
    };
    const onOpen = vi.fn();
    render(<TemplatesPage client={client} locale="en" canEdit onOpen={onOpen} />);
    const grid = within(await screen.findByRole("grid", { name: "Templates" }));

    expect(grid.getByText("NPS, short")).toBeTruthy();
    fireEvent.click(grid.getByRole("button", { name: "Start a survey from Café visit" }));
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("s8"));
    fireEvent.click(grid.getByRole("button", { name: "Start a survey from NPS, short" }));
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("s9"));
    fireEvent.click(grid.getByRole("button", { name: "Delete" }));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(calls).toContainEqual(["delete", "7f1c"]));
    expect(calls).toContainEqual(["create", { title: "NPS, short", languages: [], defaultLanguage: "", templateId: "askwerk:nps-short" }]);
  });

  it("edits our templates in the designer, copies Askwerk's to ours, and shows them as cards (W32)", async () => {
    const calls: unknown[][] = [];
    const look = (await import("@askwerk/schema")).themePresets[0]!.tokens;
    const client = {
      listTemplates: () =>
        Promise.resolve([
          { id: "askwerk:nps-short", scope: "askwerk" as const, name: null, names: { en: "NPS, short" }, questions: 3, languages: ["de", "en"], front: { title: { en: "How likely?" }, text: null, defaultLanguage: "en", look } },
          { id: "7f1c", scope: "organisation" as const, name: "Acme check-in", names: null, questions: 14, languages: ["de"], front: { title: { de: "Willkommen bei Acme" }, text: null, defaultLanguage: "de", look } },
        ]),
      listSamples: () => Promise.resolve([]),
      create: () => Promise.resolve("s9"),
      createSample: () => Promise.resolve("s8"),
      deleteTemplate: () => Promise.resolve(),
      editTemplate: (id: string) => (calls.push(["edit", id]), Promise.resolve({ id, surveyId: "e1" })),
      copyTemplate: (id: string, language: string) => (calls.push(["copy", id, language]), Promise.resolve({ id: "c1", surveyId: "e2" })),
    };
    const onOpen = vi.fn();
    const { container } = render(<TemplatesPage client={client} locale="en" canEdit onOpen={onOpen} userId="u1" organisation="Acme GmbH" />);
    const grid = within(await screen.findByRole("grid", { name: "Templates" }));

    fireEvent.click(grid.getByRole("button", { name: "Edit Acme check-in" }));
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("e1"));
    fireEvent.click(grid.getByRole("button", { name: "Copy NPS, short to our templates" }));
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("e2"));
    expect(calls).toEqual([["edit", "7f1c"], ["copy", "askwerk:nps-short", "en"]]);

    fireEvent.click(screen.getByRole("button", { name: "Ours 1" }));
    fireEvent.click(screen.getByRole("button", { name: "▦ Cards" }));
    const cards = within(screen.getByRole("list", { name: "Templates" }));
    expect(cards.getAllByRole("listitem")).toHaveLength(1);
    expect(cards.getByText("Willkommen bei Acme")).toBeTruthy();
    await expectNoAxeViolations(container);
  });
});
