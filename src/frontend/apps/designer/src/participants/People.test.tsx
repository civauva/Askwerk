// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ContactsPage } from "../contacts/ContactsPage";
import { ImportDialog } from "./ImportDialog";
import { PersonPanel } from "./PersonPanel";
import { FakeContactsClient, FakeParticipantsClient } from "./testing";

afterEach(cleanup);

/** Picks an option of a shared combo box: open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  fireEvent.click(scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) }));
  fireEvent.click(screen.getByRole("option", { name: option }));
}

describe("Import people (W35)", () => {
  it("reads a pasted table, maps the columns, checks them and invites with External ID and fields", async () => {
    const client = new FakeParticipantsClient();
    const onImport = vi.fn(() => Promise.resolve("2 added."));
    const onClose = vi.fn();
    const { container } = render(
      <ImportDialog locale="en" mode="participants" read={(source) => client.table("s1", source)} check={(people) => client.checkPeople("s1", people)} onImport={onImport} onClose={onClose} />,
    );

    fireEvent.change(screen.getByRole("textbox", { name: "or paste a table from Excel, the header first" }), { target: { value: "E-Mail-Adresse\tVorname\nlena@acme.at\tLena" } });
    fireEvent.click(screen.getByRole("button", { name: "Read" }));
    const map = within(await screen.findByRole("table", { name: "Which column is what" }));

    expect(map.getByRole("button", { name: /^Kundennummer External ID · matches people/ })).toBeTruthy();
    expect(map.getByText("lena@acme.at, thomas@acme.at, broken, …")).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    const dialog = within(await screen.findByRole("dialog", { name: "Import people" }));
    expect(dialog.getByText("already invited (same External ID or email) · skipped").textContent).toBe("1already invited (same External ID or email) · skipped");
    expect(dialog.getByText("not an email address").textContent).toBe("1not an email address");
    expect(dialog.getByText(/\{abteilung\}/)).toBeTruthy();
    fireEvent.click(dialog.getByRole("checkbox", { name: "Also save them in Contacts" }));
    fireEvent.change(dialog.getByRole("textbox", { name: "Group" }), { target: { value: "Customers Q4" } });
    fireEvent.click(dialog.getByRole("button", { name: "Import 1 people" }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith("2 added."));
    expect(onImport).toHaveBeenCalledWith([{ email: "thomas@acme.at", name: "Thomas Huber", language: null, externalId: "10871", fields: { Abteilung: "Service" } }], { sendNow: true, saveToContacts: true, group: "Customers Q4" });
  });

  it("asks for an email column before the check", async () => {
    const client = new FakeParticipantsClient();
    render(<ImportDialog locale="en" mode="contacts" read={(source) => client.table("s1", source)} onImport={() => Promise.resolve("")} onClose={() => undefined} />);
    fireEvent.change(screen.getByRole("textbox", { name: "or paste a table from Excel, the header first" }), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Read" }));
    const map = within(await screen.findByRole("table", { name: "Which column is what" }));

    choose(map, "E-Mail-Adresse", "Leave out");
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByRole("alert").textContent).toBe("Choose which column is the email.");
  });
});

describe("One person (W35)", () => {
  it("shows the details and history, changes them, says when an address is taken, and turns reminders off", async () => {
    const client = new FakeParticipantsClient();
    const { container } = render(
      <PersonPanel client={client} surveyId="s1" participantId="p2" locale="en" timeZone="UTC" statusText="Started" writeClipboard={() => Promise.resolve()} onClose={() => undefined} onRemove={() => undefined} />,
    );
    const panel = within(await screen.findByRole("dialog", { name: "Josef Huber" }));

    expect((panel.getByRole("textbox", { name: "External ID" }) as HTMLInputElement).value).toBe("CRM-2");
    expect((panel.getByRole("textbox", { name: "department" }) as HTMLInputElement).value).toBe("Sales");
    expect(panel.getAllByRole("listitem").map((item) => item.firstChild?.textContent)).toEqual(["Invited", "Invitation sent", "Reminder 1 sent", "Next: automatic reminder"]);
    await expectNoAxeViolations(container);
    fireEvent.change(panel.getByRole("textbox", { name: "Email" }), { target: { value: "taken@acme.at" } });
    fireEvent.click(panel.getByRole("button", { name: "Save" }));
    expect(await panel.findByText("Someone else in this survey has this address.")).toBeTruthy();
    fireEvent.change(panel.getByRole("textbox", { name: "Email" }), { target: { value: "josef.huber@acme.at" } });
    fireEvent.click(panel.getByRole("button", { name: "Save" }));
    expect(await panel.findByText("Saved.")).toBeTruthy();
    fireEvent.click(panel.getByRole("button", { name: "No more reminders" }));

    await waitFor(() => expect(client.calls).toContainEqual(["setRemindersOff", "s1", "p2", true]));
    expect(client.calls).toContainEqual(["change", "s1", "p2", { email: "josef.huber@acme.at", name: "Josef Huber", language: "de", externalId: "CRM-2", fields: { department: "Sales" } }]);
  });
});

describe("Contacts (W35)", () => {
  const surveys = { list: () => Promise.resolve({ items: [{ id: "s1", title: "Customer satisfaction 2026", status: "published" as const, publishedVersion: 1, hasChanges: false, languages: ["de"], createdBy: "Anna", updatedAt: "2026-10-01T00:00:00Z", responses: 3 }], counts: { all: 1, draft: 0, published: 1, closed: 0 }, total: 1 }) };

  it("lists contacts with groups, External ID and who can be invited, and invites the chosen to a survey", async () => {
    const client = new FakeContactsClient();
    const participants = new FakeParticipantsClient();
    const { container } = render(<ContactsPage client={client} participants={participants} surveys={surveys} locale="en" canEdit />);
    const grid = within(await screen.findByRole("grid", { name: "Contacts" }));

    expect(await grid.findByText("CRM-10442")).toBeTruthy();
    expect(grid.getByText("No · unsubscribed")).toBeTruthy();
    expect(within(screen.getByRole("navigation", { name: "Groups" })).getByRole("button", { name: /^Key accounts/ })).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(grid.getByRole("checkbox", { name: "Select: lena@acme.at" }));
    fireEvent.click(within(screen.getByRole("region", { name: "1 selected" })).getByRole("button", { name: "Invite to a survey…" }));
    fireEvent.click(await screen.findByRole("button", { name: "Invite 1" }));

    await waitFor(() => expect(participants.calls.find(([name]) => name === "invite")).toEqual(["invite", "s1", { participants: [], contactIds: ["c1"] }]));
  });

  it("filters by group, and adds a contact with an External ID", async () => {
    const client = new FakeContactsClient();
    render(<ContactsPage client={client} participants={new FakeParticipantsClient()} surveys={surveys} locale="en" canEdit />);
    await screen.findByText("CRM-10442");

    fireEvent.click(within(screen.getByRole("navigation", { name: "Groups" })).getByRole("button", { name: /^Key accounts/ }));
    await waitFor(() => expect(client.calls.at(-1)).toEqual(["list", { group: "Key accounts", search: "", offset: 0, limit: 50 }]));
    fireEvent.click(screen.getByRole("button", { name: "+ Add contact" }));
    const dialog = within(screen.getByRole("dialog", { name: "Add contact" }));
    fireEvent.change(dialog.getByRole("textbox", { name: "Email" }), { target: { value: "sarah@acme.at" } });
    fireEvent.change(dialog.getByRole("textbox", { name: "External ID" }), { target: { value: "CRM-11003" } });
    fireEvent.change(dialog.getByRole("textbox", { name: "Groups" }), { target: { value: "Key accounts, Event" } });
    fireEvent.click(dialog.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(client.calls.find(([name]) => name === "create")).toEqual(["create", { email: "sarah@acme.at", name: null, language: null, externalId: "CRM-11003", fields: {}, groups: ["Key accounts", "Event"] }]));
  });
});
