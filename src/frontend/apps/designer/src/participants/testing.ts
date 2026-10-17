// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written ParticipantsClient for the invite dialog's and the participants list's tests.
import { ApiError, type ContactList, type ContactsClient, type InviteRequest, type ParticipantCheck, type ParticipantDetail, type ParticipantInput, type ParticipantList, type ParticipantQuery, type ParticipantsClient, type ParticipantSelection, type PeopleTable } from "@askwerk/app-client";

export const sampleList: ParticipantList = {
  items: [
    { id: "p1", email: "anna@acme.at", name: "Anna Berger", language: "de", status: "answered", sentAt: "2026-10-14T08:00:00Z", reminders: 0, link: "https://surveys.acme.at/s/kz26b7mq?t=aaa", externalId: "CRM-1", fields: { department: "Sales" }, lastActivity: "2026-10-15T09:00:00Z", lastActivityKind: "answered", invitedAt: "2026-10-14T07:59:00Z" },
    { id: "p2", email: "josef@acme.at", name: "Josef Huber", language: null, status: "started", sentAt: "2026-10-14T08:00:00Z", reminders: 1, link: "https://surveys.acme.at/s/kz26b7mq?t=bbb", remindedAt: "2026-10-21T08:00:00Z", lastActivity: "2026-10-16T10:00:00Z", lastActivityKind: "started", invitedAt: "2026-10-14T07:59:00Z", page: 2, pages: 4 },
    { id: "p3", email: "old@acme.at", name: null, language: null, status: "bounced", sentAt: null, reminders: 0, link: "https://surveys.acme.at/s/kz26b7mq?t=ccc", lastActivity: "2026-10-14T08:00:00Z", lastActivityKind: "bounced", bounceReason: "mailbox-full", invitedAt: "2026-10-14T07:59:00Z" },
  ],
  total: 3,
  counts: { answered: 1, started: 1, bounced: 1 },
  reminderAt: "2026-10-25T08:00:00Z",
  matching: 3,
  fieldKeys: ["department"],
  autoReminders: { on: true, days: 7, max: 2, nextAt: "2026-10-23T08:00:00Z", nextCount: 2 },
};

/** A pasted sheet as the server reads it (W35). */
export const sampleTable: PeopleTable = {
  fileName: "customers.csv",
  columns: ["E-Mail-Adresse", "Vorname", "Nachname", "Kundennummer", "Abteilung"],
  guesses: ["email", "firstName", "lastName", "externalId", "field"],
  rows: [
    ["lena@acme.at", "Lena", "Gruber", "10442", "Sales"],
    ["thomas@acme.at", "Thomas", "Huber", "10871", "Service"],
    ["broken", "X", "Y", "1", "Sales"],
  ],
};

export const sampleDetail: ParticipantDetail = {
  id: "p2",
  email: "josef@acme.at",
  name: "Josef Huber",
  language: "de",
  externalId: "CRM-2",
  fields: { department: "Sales" },
  link: "https://surveys.acme.at/s/kz26b7mq?t=bbb",
  remindersOff: false,
  contactId: null,
  history: [
    { kind: "invited", at: "2026-10-14T07:59:00Z", count: null, source: "import:customers-q4.csv" },
    { kind: "sent", at: "2026-10-14T08:00:00Z", count: null },
    { kind: "reminded", at: "2026-10-21T08:00:00Z", count: 1, automatic: true },
  ],
  nextReminderAt: "2026-10-28T08:00:00Z",
  autoReminders: true,
};

export class FakeParticipantsClient implements ParticipantsClient {
  readonly calls: [string, ...unknown[]][] = [];
  emailReady = true;
  /** How many the survey has, so a test can page through more than the three shown. */
  total = sampleList.total;
  list = (surveyId: string, query?: ParticipantQuery) => {
    this.calls.push(query === undefined ? ["list", surveyId] : ["list", surveyId, query]);
    return Promise.resolve({ ...sampleList, total: this.total, matching: this.total });
  };

  checkText = (surveyId: string, text: string) => {
    this.calls.push(["checkText", surveyId, text]);
    const lines = text.split("\n").map((line) => line.trim()).filter(Boolean);
    const seen = new Set<string>();
    const check: ParticipantCheck = { participants: [], duplicates: 0, alreadyInvited: 0, unsubscribed: 0, invalid: [] };
    const result = { ...check, participants: [] as { email: string; name: string | null; language: string | null }[], invalid: [] as { line: number; text: string }[], duplicates: 0 };
    lines.forEach((line, index) => {
      if (!/^[^@\s]+@[^@\s]+\.[a-z]{2,}$/.test(line)) {
        result.invalid.push({ line: index + 1, text: line });
      } else if (seen.has(line)) {
        result.duplicates += 1;
      } else {
        seen.add(line);
        result.participants.push({ email: line, name: null, language: null });
      }
    });
    return Promise.resolve<ParticipantCheck>(result);
  };

  checkFile = (surveyId: string, file: File) => {
    this.calls.push(["checkFile", surveyId, file.name]);
    return Promise.resolve<ParticipantCheck>({ participants: [{ email: "lena@acme.at", name: "Lena Koch", language: "en" }], duplicates: 0, alreadyInvited: 1, unsubscribed: 0, invalid: [] });
  };

  invite = (surveyId: string, request: InviteRequest) => {
    this.calls.push(["invite", surveyId, request]);

    if (request.send !== false && !this.emailReady) {
      return Promise.reject(new ApiError(409, undefined, "conflict", { code: "email-not-set-up" }));
    }

    return Promise.resolve({ invited: request.participants.length, skipped: 0 });
  };

  remind = (surveyId: string, selection?: ParticipantSelection) => {
    this.calls.push(selection === undefined ? ["remind", surveyId] : ["remind", surveyId, selection]);
    return this.emailReady ? Promise.resolve() : Promise.reject(new ApiError(409, undefined, "conflict", { code: "email-not-set-up" }));
  };

  remove = (surveyId: string, selection: ParticipantSelection) => {
    this.calls.push(["remove", surveyId, selection]);
    const chosen = "all" in selection ? this.total : selection.participantIds.length;
    return Promise.resolve({ removed: chosen - 1, kept: 1 });
  };

  linksUrl = (surveyId: string) => `/api/app/surveys/${surveyId}/participants/links.csv`;

  table = (surveyId: string, source: File | string) => {
    this.calls.push(["table", surveyId, typeof source === "string" ? source : source.name]);
    return Promise.resolve(sampleTable);
  };

  checkPeople = (surveyId: string, people: readonly ParticipantInput[]) => {
    this.calls.push(["checkPeople", surveyId, people]);
    const valid = people.filter((person) => person.email.includes("@"));
    return Promise.resolve<ParticipantCheck>({ participants: valid.slice(1), duplicates: 0, alreadyInvited: 1, unsubscribed: 0, invalid: people.filter((person) => !person.email.includes("@")).map((person, index) => ({ line: index + 1, text: person.email })) });
  };

  detail = (surveyId: string, participantId: string) => {
    this.calls.push(["detail", surveyId, participantId]);
    return Promise.resolve({ ...sampleDetail, id: participantId });
  };

  change = (surveyId: string, participantId: string, person: ParticipantInput) => {
    this.calls.push(["change", surveyId, participantId, person]);
    return person.email === "taken@acme.at" ? Promise.reject(new ApiError(400, undefined, "invalid", { errors: { email: ["email-taken"] } })) : Promise.resolve();
  };

  setRemindersOff = (surveyId: string, participantId: string, off: boolean) => {
    this.calls.push(["setRemindersOff", surveyId, participantId, off]);
    return Promise.resolve();
  };

  resend = (surveyId: string, selection: ParticipantSelection) => {
    this.calls.push(["resend", surveyId, selection]);
    return Promise.resolve();
  };

  saveToContacts = (surveyId: string, selection: ParticipantSelection, group: string | null) => {
    this.calls.push(["saveToContacts", surveyId, selection, group]);
    return Promise.resolve({ added: 1, updated: 1 });
  };

  exportUrl = (surveyId: string, query?: ParticipantQuery) => `/api/app/surveys/${surveyId}/participants.csv${query?.status === undefined ? "" : `?status=${query.status}`}`;

  exportChosen = (surveyId: string, selection: ParticipantSelection) => {
    this.calls.push(["exportChosen", surveyId, selection]);
    return Promise.resolve({ blob: new Blob(["email"]), fileName: "participants.csv" });
  };

  setAutoReminders = (surveyId: string, on: boolean, days?: number, max?: number) => {
    this.calls.push(["setAutoReminders", surveyId, on, days, max]);
    return Promise.resolve({ on, days: days ?? 7, max: max ?? 2, nextAt: null, nextCount: 0 });
  };
}

export const sampleContacts: ContactList = {
  items: [
    { id: "c1", email: "lena@acme.at", name: "Lena Gruber", language: "de", externalId: "CRM-10442", fields: { department: "Sales" }, groups: ["Key accounts"], surveys: 3, answered: 2, lastInvitedAt: "2026-10-02T08:00:00Z", invitable: true, unsubscribed: false, bounced: false },
    { id: "c2", email: "anja@acme.at", name: "Anja Weber", language: "de", externalId: null, fields: {}, groups: [], surveys: 1, answered: 0, lastInvitedAt: null, invitable: false, unsubscribed: true, bounced: false, unsubscribedAt: "2026-10-04T08:00:00Z" },
  ],
  matching: 2,
  counts: { all: 2, invitable: 1, unsubscribed: 1, bounced: 0 },
  groups: [{ name: "Key accounts", count: 1 }],
  fieldKeys: ["department"],
};

/** A hand-written ContactsClient (W35). */
export class FakeContactsClient implements ContactsClient {
  readonly calls: [string, ...unknown[]][] = [];
  list = (query?: Parameters<ContactsClient["list"]>[0]) => {
    this.calls.push(["list", query]);
    return Promise.resolve(sampleContacts);
  };
  create = (contact: Parameters<ContactsClient["create"]>[0]) => (this.calls.push(["create", contact]), Promise.resolve("c9"));
  change = (id: string, contact: Parameters<ContactsClient["change"]>[1]) => (this.calls.push(["change", id, contact]), Promise.resolve());
  remove = (ids: readonly string[]) => (this.calls.push(["remove", ids]), Promise.resolve({ removed: ids.length }));
  group = (ids: readonly string[], group: string, leave?: boolean) => (this.calls.push(["group", ids, group, leave]), Promise.resolve({ changed: ids.length }));
  import = (people: readonly ParticipantInput[], group: string | null) => (this.calls.push(["import", people, group]), Promise.resolve({ added: people.length, updated: 0, invalid: [] }));
  exportUrl = (group?: string) => `/api/app/contacts/contacts.csv${group ? `?group=${group}` : ""}`;
  exportChosen = (ids: readonly string[]) => (this.calls.push(["exportChosen", ids]), Promise.resolve({ blob: new Blob(["email"]), fileName: "contacts.csv" }));
  table = (source: File | string) => (this.calls.push(["table", typeof source === "string" ? source : source.name]), Promise.resolve(sampleTable));
}
