// SPDX-License-Identifier: AGPL-3.0-only
import { createDownload, createRequest, type AppClientOptions } from "./client";

/** A person to invite. */
export interface ParticipantInput {
  readonly email: string;
  readonly name: string | null;
  readonly language: string | null;
  /** Their id in another system (W35); matches them before the email does. */
  readonly externalId?: string | null;
  /** Further fields (W35), placeholders in messages, e.g. { department: "Sales" }. */
  readonly fields?: Readonly<Record<string, string>> | null;
}

/** Who a list would invite (W12 C step 1). */
export interface ParticipantCheck {
  readonly participants: readonly ParticipantInput[];
  readonly duplicates: number;
  readonly alreadyInvited: number;
  readonly unsubscribed: number;
  readonly invalid: readonly { readonly line: number; readonly text: string }[];
}

/** An invitation to send (W12 C step 2). */
export interface InviteRequest {
  readonly participants: readonly ParticipantInput[];
  /** ISO time; now when empty. */
  readonly sendAt?: string;
  /** yyyy-MM-dd, for {deadline}. */
  readonly deadline?: string;
  readonly reminder?: boolean;
  /** False makes personal links only (email not set up). */
  readonly send?: boolean;
  readonly host?: string;
  /** Contacts to invite as well (W35). */
  readonly contactIds?: readonly string[];
  /** Also keep the people in Contacts (W35), in a group. */
  readonly saveToContacts?: boolean;
  readonly contactGroup?: string;
}

export type ParticipantStatus = "waiting" | "link-only" | "sent" | "opened" | "started" | "answered" | "bounced" | "unsubscribed";

export interface ParticipantItem {
  readonly id: string;
  readonly email: string;
  readonly name: string | null;
  readonly language: string | null;
  readonly status: ParticipantStatus;
  readonly sentAt: string | null;
  readonly reminders: number;
  readonly link: string;
  readonly externalId?: string | null;
  readonly fields?: Readonly<Record<string, string>> | null;
  /** The last thing that happened: answering, opening, a reminder, the invitation (W35). */
  readonly lastActivity?: string | null;
  readonly remindedAt?: string | null;
  readonly remindersOff?: boolean;
  readonly contactId?: string | null;
}

/** The automatic reminders of a survey (W35) and the next one: when and to how many. */
export interface AutoReminders {
  readonly on: boolean;
  readonly days: number;
  readonly max: number;
  readonly nextAt: string | null;
  readonly nextCount: number;
}

/** A list read as a table (W35): columns, a guess for each, and every row. */
export interface PeopleTable {
  readonly fileName: string | null;
  readonly columns: readonly string[];
  readonly guesses: readonly ("email" | "name" | "firstName" | "lastName" | "language" | "externalId" | "field")[];
  readonly rows: readonly (readonly string[])[];
}

/** One thing that happened to a person (W35). */
export interface ParticipantEvent {
  readonly kind: "invited" | "sent" | "reminded" | "opened" | "started" | "answered" | "bounced" | "unsubscribed";
  readonly at: string;
  readonly count: number | null;
}

/** One person (W35): details, personal link and history. */
export interface ParticipantDetail {
  readonly id: string;
  readonly email: string;
  readonly name: string | null;
  readonly language: string | null;
  readonly externalId: string | null;
  readonly fields: Readonly<Record<string, string>>;
  readonly link: string;
  readonly remindersOff: boolean;
  readonly contactId: string | null;
  readonly history: readonly ParticipantEvent[];
  readonly nextReminderAt: string | null;
  readonly autoReminders: boolean;
}

/** One page of the participants (W13 C); total counts all of the survey's. */
export interface ParticipantList {
  readonly items: readonly ParticipantItem[];
  readonly total: number;
  readonly counts: Readonly<Partial<Record<ParticipantStatus, number>>>;
  readonly reminderAt: string | null;
  /** How many the search and filters choose (W35). */
  readonly matching?: number;
  /** The fields the survey's participants have (W35), to filter by. */
  readonly fieldKeys?: readonly string[];
  readonly autoReminders?: AutoReminders | null;
}

/** sort: name, status or sent; a leading minus sorts descending (newest first by default). */
/** status: one status, or not-answered; field: key:value (W35). */
export type ParticipantQuery = {
  readonly sort?: string;
  readonly offset?: number;
  readonly limit?: number;
  readonly search?: string;
  readonly status?: string;
  readonly field?: string;
};

/** Chosen participants, or all of the survey's. */
export type ParticipantSelection = { readonly participantIds: readonly string[] } | { readonly all: true };

/** Invitations of a survey (AWK-7, W12 C and D). Failures are ApiError; 409 email-not-set-up when sending needs email. */
export interface ParticipantsClient {
  checkText(surveyId: string, text: string): Promise<ParticipantCheck>;
  checkFile(surveyId: string, file: File): Promise<ParticipantCheck>;
  invite(surveyId: string, request: InviteRequest): Promise<{ invited: number; skipped: number }>;
  list(surveyId: string, query?: ParticipantQuery): Promise<ParticipantList>;
  /** Reminds those who have not answered: the chosen ones, or everyone. */
  remind(surveyId: string, selection?: ParticipantSelection): Promise<void>;
  /** Removes the chosen ones; people with a response are kept. */
  remove(surveyId: string, selection: ParticipantSelection): Promise<{ removed: number; kept: number }>;
  /** The personal links as CSV, to download. */
  linksUrl(surveyId: string): string;
  /** A file or a pasted table read as columns (W35). */
  table(surveyId: string, source: File | string): Promise<PeopleTable>;
  /** Checks mapped people (W35): new, already invited (External ID or email), unsubscribed, duplicates, no address. */
  checkPeople(surveyId: string, people: readonly ParticipantInput[]): Promise<ParticipantCheck>;
  detail(surveyId: string, participantId: string): Promise<ParticipantDetail>;
  /** Changes a person; 400 email-invalid, email-taken or external-id-taken at the field. */
  change(surveyId: string, participantId: string, person: ParticipantInput): Promise<void>;
  setRemindersOff(surveyId: string, participantId: string, off: boolean): Promise<void>;
  /** The invitation again to the chosen people. */
  resend(surveyId: string, selection: ParticipantSelection): Promise<void>;
  /** The chosen people into Contacts, in a group. */
  saveToContacts(surveyId: string, selection: ParticipantSelection, group: string | null): Promise<{ added: number; updated: number }>;
  /** Every participant (with the list's search and filters) as CSV. */
  exportUrl(surveyId: string, query?: ParticipantQuery): string;
  /** The chosen participants as CSV. */
  exportChosen(surveyId: string, selection: ParticipantSelection): Promise<{ blob: Blob; fileName: string }>;
  setAutoReminders(surveyId: string, on: boolean, days?: number, max?: number): Promise<AutoReminders>;
}

function listQuery({ sort, offset, limit, search, status, field }: ParticipantQuery = {}): string {
  const query = new URLSearchParams();
  sort && query.set("sort", sort);
  offset && query.set("offset", String(offset));
  limit && query.set("limit", String(limit));
  search?.trim() && query.set("search", search.trim());
  status && query.set("status", status);
  field && query.set("field", field);
  const text = query.toString();

  return text ? `?${text}` : "";
}

export function createParticipantsClient(options: AppClientOptions): ParticipantsClient {
  const request = createRequest(options);
  const download = createDownload(options);
  const at = (surveyId: string) => `/api/app/surveys/${encodeURIComponent(surveyId)}`;

  return {
    checkText: (surveyId, text) => request<ParticipantCheck>("POST", `${at(surveyId)}/participants/check`, { text }),
    checkFile: (surveyId, file) => {
      const form = new FormData();
      form.append("file", file);
      return request<ParticipantCheck>("POST", `${at(surveyId)}/participants/check`, form);
    },
    invite: (surveyId, body) => request<{ invited: number; skipped: number }>("POST", `${at(surveyId)}/invitations`, body),
    list: (surveyId, query = {}) => request<ParticipantList>("GET", `${at(surveyId)}/participants${listQuery(query)}`),
    remind: (surveyId, selection) => request("POST", `${at(surveyId)}/participants/remind`, selection),
    remove: (surveyId, selection) => request<{ removed: number; kept: number }>("POST", `${at(surveyId)}/participants/remove`, selection),
    linksUrl: (surveyId) => `${at(surveyId)}/participants/links.csv`,
    table: (surveyId, source) => {
      if (typeof source === "string") {
        return request<PeopleTable>("POST", `${at(surveyId)}/participants/table`, { text: source });
      }

      const form = new FormData();
      form.append("file", source);
      return request<PeopleTable>("POST", `${at(surveyId)}/participants/table`, form);
    },
    checkPeople: (surveyId, people) => request<ParticipantCheck>("POST", `${at(surveyId)}/participants/check-people`, { people }),
    detail: (surveyId, participantId) => request<ParticipantDetail>("GET", `${at(surveyId)}/participants/${encodeURIComponent(participantId)}`),
    change: (surveyId, participantId, person) => request("PUT", `${at(surveyId)}/participants/${encodeURIComponent(participantId)}`, person),
    setRemindersOff: (surveyId, participantId, off) => request("PUT", `${at(surveyId)}/participants/${encodeURIComponent(participantId)}/reminders`, { off }),
    resend: (surveyId, selection) => request("POST", `${at(surveyId)}/participants/resend`, selection),
    saveToContacts: (surveyId, selection, group) => request<{ added: number; updated: number }>("POST", `${at(surveyId)}/participants/contacts`, { ...selection, group }),
    exportUrl: (surveyId, query) => `${at(surveyId)}/participants.csv${listQuery(query)}`,
    exportChosen: (surveyId, selection) => download("POST", `${at(surveyId)}/participants.csv`, selection),
    setAutoReminders: (surveyId, on, days, max) => request<AutoReminders>("PUT", `${at(surveyId)}/reminders`, { on, days, max }),
  };
}
