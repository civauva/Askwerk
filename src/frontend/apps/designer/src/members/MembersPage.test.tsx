// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MembersPage } from "./MembersPage";
import { FakeMembersClient, sampleMembers } from "./testing";

afterEach(cleanup);

async function shown(client = new FakeMembersClient(), locale: "de" | "en" = "en", writeClipboard = vi.fn(() => Promise.resolve())) {
  const view = render(<MembersPage client={client} locale={locale} organisation="Acme GmbH" writeClipboard={writeClipboard} timeZone="UTC" now={() => new Date("2026-10-15T12:00:00Z")} />);
  await screen.findByRole("grid", { name: locale === "de" ? "Mitglieder" : "Members" });
  await act(async () => {});

  return { client, writeClipboard, ...view };
}

const row = (name: string) => within(screen.getByRole("row", { name: new RegExp(name) }));

describe("Members (AWK-30, W18 A)", () => {
  it("lists the members with their role, two-factor and last sign-in, and the open invitations", async () => {
    const { container } = await shown();

    expect(screen.getByText("4 members · 1 invitation open")).toBeTruthy();
    expect(row("Dražan Kosić").queryByRole("button", { name: /^Role of/ })).toBeNull();
    expect(row("Dražan Kosić").getByText("Owner")).toBeTruthy();
    expect(row("Ben Huber").getByRole("button", { name: /^Role of Ben Huber/ }).textContent).toContain("Editor");
    expect(row("Ben Huber").getByText("Off")).toBeTruthy();
    expect(row("Josef Huber").getByText("never")).toBeTruthy();
    const invitations = within(screen.getByRole("region", { name: "Invitations open" }));
    expect(invitations.getByText("eva@acme.at")).toBeTruthy();
    expect(invitations.getByText("Anna Berger · Oct 15 · valid 7 more days")).toBeTruthy();
    await expectNoAxeViolations(container);
  });

  it("locks members a host system manages (AWK-12, W22)", async () => {
    const client = new FakeMembersClient();
    client.page = { ...sampleMembers, members: sampleMembers.members.map((member) => (member.id === "u3" ? { ...member, managedBy: "HR Suite" } : member)) };
    await shown(client);

    expect(row("Ben Huber").getByText("Managed by HR Suite")).toBeTruthy();
    expect(row("Ben Huber").queryByRole("button", { name: /^Role of/ })).toBeNull();
    expect(row("Ben Huber").queryByRole("button", { name: /^Remove/ })).toBeNull();
  });

  it("changes a role from the row", async () => {
    const { client } = await shown();

    fireEvent.click(row("Ben Huber").getByRole("button", { name: /^Role of Ben Huber/ }));
    fireEvent.click(screen.getByRole("option", { name: "Viewer" }));

    await waitFor(() => expect(client.calls).toContainEqual(["changeRole", "u3", "viewer"]));
    expect(client.calls.filter(([name]) => name === "list")).toHaveLength(2);
  });

  it("leaves owners to owners: an admin cannot change or remove them, nor give the owner role", async () => {
    const client = new FakeMembersClient();
    client.page = { ...sampleMembers, canManageOwners: false, members: sampleMembers.members.map((member) => ({ ...member, you: member.id === "u2" })) };
    await shown(client);

    expect(row("Dražan Kosić").queryByRole("button", { name: /^Role of/ })).toBeNull();
    expect(row("Dražan Kosić").queryByRole("button", { name: /^Remove/ })).toBeNull();
    fireEvent.click(row("Ben Huber").getByRole("button", { name: /^Role of Ben Huber/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Admin", "Editor", "Analyst", "Viewer"]);
  });

  it("removes a member after asking", async () => {
    const { client } = await shown();

    fireEvent.click(row("Ben Huber").getByRole("button", { name: "Remove Ben Huber…" }));
    const ask = within(screen.getByRole("dialog", { name: "Remove member" }));
    expect(ask.getByText("Remove Ben Huber from Acme GmbH? Their surveys and results stay.")).toBeTruthy();
    fireEvent.click(ask.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(client.calls).toContainEqual(["remove", "u3"]));
  });

  it("copies an invitation's link and revokes one", async () => {
    const { client, writeClipboard } = await shown();
    const invitations = within(screen.getByRole("region", { name: "Invitations open" }));

    fireEvent.click(invitations.getByRole("button", { name: "Copy link for eva@acme.at" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith("https://surveys.acme.at/join/abc"));
    expect(invitations.queryByRole("button", { name: /^Send again/ })).toBeNull();
    fireEvent.click(invitations.getByRole("button", { name: "Revoke invitation for eva@acme.at" }));

    await waitFor(() => expect(client.calls).toContainEqual(["revoke", "i1"]));
  });

  it("tells editors, analysts and viewers who manages members", async () => {
    const client = new FakeMembersClient();
    client.refuse = true;
    render(<MembersPage client={client} locale="en" organisation="Acme GmbH" />);

    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Only owners and admins manage members.");
  });
});

describe("Invite members (AWK-30, W18 B)", () => {
  it("checks the addresses as you type and gives the links to copy without email", async () => {
    const { client, writeClipboard } = await shown(new FakeMembersClient(), "de");

    fireEvent.click(screen.getByRole("button", { name: "Mitglieder einladen…" }));
    const dialog = within(screen.getByRole("dialog", { name: "Mitglieder einladen" }));
    fireEvent.change(dialog.getByRole("textbox", { name: "E-Mail-Adressen, eine pro Zeile" }), { target: { value: "eva@acme.at\ntom@partner.example\nben@acme.at\nnot-an-address" } });
    expect(dialog.getByText("2 neu · schon Mitglied: ben@acme.at · keine Adresse: not-an-address")).toBeTruthy();
    expect(dialog.getByRole("button", { name: /^Rolle/ }).textContent).toContain("Bearbeiten (Editor)");
    fireEvent.click(dialog.getByRole("button", { name: "2 einladen" }));

    await waitFor(() => expect(client.calls).toContainEqual(["invite", ["eva@acme.at", "tom@partner.example"], "editor"]));
    expect(await dialog.findByText("E-Mail ist noch nicht eingerichtet. Kopieren Sie die Links und geben Sie sie weiter.")).toBeTruthy();
    fireEvent.click(dialog.getByRole("button", { name: "Link für eva@acme.at kopieren" }));
    await waitFor(() => expect(writeClipboard).toHaveBeenCalledWith("https://surveys.acme.at/join/token0"));
  });

  it("sends the invitations when email is set up, and says so", async () => {
    const client = new FakeMembersClient();
    client.page = { ...sampleMembers, emailReady: true };
    await shown(client);

    fireEvent.click(screen.getByRole("button", { name: "Invite members…" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Email addresses, one per line" }), { target: { value: "eva@acme.at" } });
    fireEvent.click(screen.getByRole("button", { name: "Invite 1" }));

    expect(await screen.findByText("1 invitation sent.")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
