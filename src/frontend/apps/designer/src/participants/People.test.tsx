// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ContactsPage } from "../contacts/ContactsPage";
import { groupOf, ImportDialog } from "./ImportDialog";
import { ParticipantsPanel } from "./ParticipantsPanel";
import { dayTimeText, fieldLabel, shortLink } from "./peopleFormat";
import { PersonPanel } from "./PersonPanel";
import { FakeContactsClient, FakeParticipantsClient } from "./testing";

afterEach(cleanup);

/** Picks an option of a shared combo box: open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  fireEvent.click(scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) }));
  fireEvent.click(screen.getByRole("option", { name: option }));
}

describe("The W35 formats", () => {
  it("writes dates as the mockup does, field names with a capital, links without the protocol, groups after the file", () => {
    expect(dayTimeText("en", "2026-10-02T09:14:00Z", "UTC")).toBe("2 Oct 09:14");
    expect(fieldLabel("customer_no")).toBe("Customer no");
    expect(shortLink("https://surveys.acme.at/s/k7m2pqzr?t=4hX")).toBe("surveys.acme.at/s/k7m2pqzr?t=4hX");
    expect(groupOf("customers-q4.csv")).toBe("Customers Q4");
  });
});

describe("Participants page (W35 a)", () => {
  it("shows statuses with page progress, what happened last, a Fix email for bounced people, and a boxed bar for the chosen", async () => {
    const client = new FakeParticipantsClient();
    const { container } = render(<ParticipantsPanel client={client} surveyId="s1" locale="en" timeZone="UTC" now={() => new Date("2026-10-17T12:00:00Z")} />);
    const grid = within(await screen.findByRole("grid", { name: "Participants" }));

    expect(await grid.findByText("Started · page 2 of 4")).toBeTruthy();
    expect(grid.getByText("answered 15 Oct")).toBeTruthy();
    expect(grid.getByText("mailbox full · 14 Oct")).toBeTruthy();
    expect(grid.getByText("1 · 21 Oct")).toBeTruthy();
    expect(grid.getByRole("columnheader", { name: "Department" })).toBeTruthy();
    expect(screen.getByRole("searchbox", { name: "Search name, email, field…" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Bounced 1" })).toBeTruthy();
    expect(screen.getByText("bounced").parentElement?.textContent).toBe("1 bounced");
    expect(screen.getByText("1–3 of 3")).toBeTruthy();
    await expectNoAxeViolations(container);

    fireEvent.click(grid.getByRole("button", { name: "Fix email" }));
    expect(await screen.findByRole("dialog", { name: "Josef Huber" })).toBeTruthy();
  });

  it("says what the chosen are of, and clears them with the bar's box", async () => {
    const client = new FakeParticipantsClient();
    render(<ParticipantsPanel client={client} surveyId="s1" locale="en" timeZone="UTC" />);
    const grid = within(await screen.findByRole("grid", { name: "Participants" }));

    fireEvent.click(await grid.findByRole("checkbox", { name: "Select: josef@acme.at" }));
    const bar = screen.getByRole("region", { name: "1 chosen" });
    expect(bar.textContent).toContain("1 chosen· of 1 not answered");
    expect(within(bar).getByRole("button", { name: "Remind now" })).toBeTruthy();
    fireEvent.click(within(bar).getByRole("checkbox", { name: "Clear" }));
    expect(screen.queryByRole("region", { name: "1 chosen" })).toBeNull();
  });

  it("puts the automatic reminders in one sentence and the field filter's count by the pager", async () => {
    const client = new FakeParticipantsClient();
    render(<ParticipantsPanel client={client} surveyId="s1" locale="en" timeZone="UTC" />);
    expect((await screen.findByRole("switch", { name: "Automatic reminders" })).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByRole("button", { name: /^Automatic reminders after 7 days/ })).toBeTruthy();
    expect(screen.getByText("next: 23 Oct, 08:00 to 2 people")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "+ Filter" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Value" }), { target: { value: "Sales" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply" }));
    expect(await screen.findByText("1–3 of 3 · Department: 3")).toBeTruthy();
  });
});

describe("Import people (W35 b)", () => {
  it("reads a pasted table, maps the columns, checks them on the same step and invites with External ID and fields", async () => {
    const client = new FakeParticipantsClient();
    const onImport = vi.fn(() => Promise.resolve("2 added."));
    const onClose = vi.fn();
    const { container } = render(
      <ImportDialog locale="en" mode="participants" knownFields={[]} read={(source) => client.table("s1", source)} check={(people) => client.checkPeople("s1", people)} onImport={onImport} onClose={onClose} />,
    );

    fireEvent.change(screen.getByRole("textbox", { name: "or paste a table from Excel, the header first" }), { target: { value: "E-Mail-Adresse\tVorname\nlena@acme.at\tLena" } });
    fireEvent.click(screen.getByRole("button", { name: "Read" }));
    const map = within(await screen.findByRole("table", { name: "Which column is what" }));
    const dialog = within(screen.getByRole("dialog", { name: "Import people" }));

    expect(dialog.getAllByRole("listitem").map((step) => step.textContent)).toEqual(["✓ File", "2 Columns", "3 Check", "4 Invite"]);
    expect(dialog.getByText("customers.csv · 3 rows")).toBeTruthy();
    expect(map.getByRole("button", { name: /^Kundennummer External ID · matches people/ })).toBeTruthy();
    expect(map.getByRole("button", { name: /^Abteilung Field: Abteilung \(new\)/ })).toBeTruthy();
    expect(map.getByText("lena@acme.at, thomas@acme.at, broken, …")).toBeTruthy();
    await waitFor(() => expect(dialog.getByText("already invited (same External ID or email) · skipped").textContent).toBe("1already invited (same External ID or email) · skipped"));
    expect(dialog.getByText("unsubscribed earlier · skipped").textContent).toBe("0unsubscribed earlier · skipped");
    fireEvent.click(dialog.getByRole("button", { name: "show" }));
    expect(dialog.getByText("broken")).toBeTruthy();
    expect(dialog.getByText(/^Fields become placeholders: \{abteilung\}/)).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(dialog.getByRole("checkbox", { name: "Also save them in Contacts" }));
    expect((dialog.getByRole("textbox", { name: "Group" }) as HTMLInputElement).value).toBe("Customers");
    fireEvent.change(dialog.getByRole("textbox", { name: "Group" }), { target: { value: "Customers Q4" } });
    fireEvent.click(dialog.getByRole("button", { name: "Import 1 person" }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith("2 added."));
    expect(onImport).toHaveBeenCalledWith([{ email: "thomas@acme.at", name: "Thomas Huber", language: null, externalId: "10871", fields: { Abteilung: "Service" } }], { sendNow: true, saveToContacts: true, group: "Customers Q4", fileName: "customers.csv" });
  });

  it("asks for an email column instead of the check", async () => {
    const client = new FakeParticipantsClient();
    render(<ImportDialog locale="en" mode="contacts" read={(source) => client.table("s1", source)} onImport={() => Promise.resolve("")} onClose={() => undefined} />);
    fireEvent.change(screen.getByRole("textbox", { name: "or paste a table from Excel, the header first" }), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Read" }));
    const map = within(await screen.findByRole("table", { name: "Which column is what" }));

    choose(map, "E-Mail-Adresse", "Leave out");

    expect(screen.getByRole("alert").textContent).toBe("Choose which column is the email.");
    expect((screen.getByRole("button", { name: "Import 0 contacts" }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("One person (W35 c)", () => {
  it("shows the details and history, changes them, says when an address is taken, and turns reminders off", async () => {
    const client = new FakeParticipantsClient();
    const { container } = render(
      <PersonPanel client={client} surveyId="s1" participantId="p2" locale="en" timeZone="UTC" status="started" statusText="Started" writeClipboard={() => Promise.resolve()} onClose={() => undefined} onRemove={() => undefined} />,
    );
    const panel = within(await screen.findByRole("dialog", { name: "Josef Huber" }));

    expect(panel.getAllByRole("textbox").map((box) => (box as HTMLInputElement).value)).toEqual(["josef@acme.at", "Josef Huber", "Sales", "CRM-2"]);
    expect((panel.getByRole("textbox", { name: "Department" }) as HTMLInputElement).value).toBe("Sales");
    expect(panel.getByText("surveys.acme.at/s/kz26b7mq?t=bbb")).toBeTruthy();
    expect(panel.getByRole("button", { name: "Link" }).textContent).toBe("Copy");
    expect(panel.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "Invited · added by import “customers-q4.csv”14 Oct 07:59",
      "Invitation sent14 Oct 08:00",
      "Reminder 1 sent (automatic)21 Oct 08:00",
      "Next: reminder 2 (automatic)28 Oct 08:00",
    ]);
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

describe("Contacts (W35 d)", () => {
  const surveys = { list: () => Promise.resolve({ items: [{ id: "s1", title: "Customer satisfaction 2026", status: "published" as const, publishedVersion: 1, hasChanges: false, languages: ["de"], createdBy: "Anna", updatedAt: "2026-10-01T00:00:00Z", responses: 3 }], counts: { all: 1, draft: 0, published: 1, closed: 0 }, total: 1 }) };

  it("lists contacts with groups, External ID and who can be invited, and invites the chosen to a survey", async () => {
    const client = new FakeContactsClient();
    const participants = new FakeParticipantsClient();
    const onCount = vi.fn();
    const { container } = render(<ContactsPage client={client} participants={participants} surveys={surveys} locale="en" canEdit onCount={onCount} />);
    const grid = within(await screen.findByRole("grid", { name: "Contacts" }));

    expect(await grid.findByText("CRM-10442")).toBeTruthy();
    expect(grid.getByText("No · unsubscribed 4 Oct")).toBeTruthy();
    expect(grid.getByRole("columnheader", { name: "Department" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "All" }).getAttribute("aria-pressed")).toBe("true");
    const groups = within(screen.getByRole("navigation", { name: "Groups" }));
    expect(groups.getByRole("button", { name: /^Key accounts/ })).toBeTruthy();
    expect(groups.getByText("External ID")).toBeTruthy();
    expect(onCount).toHaveBeenCalledWith(2);
    await expectNoAxeViolations(container);
    fireEvent.click(grid.getByRole("checkbox", { name: "Select: lena@acme.at" }));
    const bar = within(screen.getByRole("region", { name: "1 chosen" }));
    fireEvent.click(bar.getByRole("button", { name: "Export" }));
    await waitFor(() => expect(client.calls).toContainEqual(["exportChosen", ["c1"]]));
    fireEvent.click(bar.getByRole("button", { name: "Invite to a survey…" }));
    fireEvent.click(await screen.findByRole("button", { name: "Invite 1" }));

    await waitFor(() => expect(participants.calls.find(([name]) => name === "invite")).toEqual(["invite", "s1", { participants: [], contactIds: ["c1"] }]));
  });

  it("filters by group, makes a new group, and adds a contact with an External ID", async () => {
    const client = new FakeContactsClient();
    render(<ContactsPage client={client} participants={new FakeParticipantsClient()} surveys={surveys} locale="en" canEdit />);
    await screen.findByText("CRM-10442");
    const groups = within(screen.getByRole("navigation", { name: "Groups" }));

    fireEvent.click(groups.getByRole("button", { name: /^Key accounts/ }));
    await waitFor(() => expect(client.calls.at(-1)).toEqual(["list", { group: "Key accounts", search: "", offset: 0, limit: 50 }]));
    fireEvent.click(await screen.findByRole("checkbox", { name: "Select: lena@acme.at" }));
    expect(screen.getByRole("region", { name: "1 chosen" }).textContent).toContain("1 chosen· group Key accounts");
    fireEvent.click(screen.getByRole("checkbox", { name: "Clear" }));
    fireEvent.click(groups.getByRole("button", { name: "+ New group" }));
    fireEvent.change(groups.getByRole("textbox", { name: "Group" }), { target: { value: "Event Vienna 2026" } });
    fireEvent.submit(groups.getByRole("textbox", { name: "Group" }));
    expect(groups.getByRole("button", { name: /^Event Vienna 2026/ }).getAttribute("aria-current")).toBe("true");

    fireEvent.click(screen.getByRole("button", { name: "+ Add contact" }));
    const dialog = within(screen.getByRole("dialog", { name: "Add contact" }));
    fireEvent.change(dialog.getByRole("textbox", { name: "Email" }), { target: { value: "sarah@acme.at" } });
    fireEvent.change(dialog.getByRole("textbox", { name: "External ID" }), { target: { value: "CRM-11003" } });
    fireEvent.change(dialog.getByRole("textbox", { name: "Groups" }), { target: { value: "Key accounts, Event" } });
    fireEvent.click(dialog.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(client.calls.find(([name]) => name === "create")).toEqual(["create", { email: "sarah@acme.at", name: null, language: null, externalId: "CRM-11003", fields: {}, groups: ["Key accounts", "Event"] }]));
  });
});
