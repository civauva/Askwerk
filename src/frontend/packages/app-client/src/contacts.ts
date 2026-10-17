// SPDX-License-Identifier: AGPL-3.0-only
import { createDownload, createRequest, type AppClientOptions } from "./client";
import type { ParticipantInput, PeopleTable } from "./participants";

/** A contact in the organisation's address book (W35). */
export interface ContactItem {
  readonly id: string;
  readonly email: string;
  readonly name: string | null;
  readonly language: string | null;
  readonly externalId: string | null;
  readonly fields: Readonly<Record<string, string>>;
  readonly groups: readonly string[];
  /** Surveys they were invited to, and answered. */
  readonly surveys: number;
  readonly answered: number;
  readonly lastInvitedAt: string | null;
  /** Neither unsubscribed nor bounced. */
  readonly invitable: boolean;
  readonly unsubscribed: boolean;
  readonly bounced: boolean;
  readonly unsubscribedAt?: string | null;
}

export interface ContactList {
  readonly items: readonly ContactItem[];
  readonly matching: number;
  readonly counts: Readonly<Record<"all" | "invitable" | "unsubscribed" | "bounced", number>>;
  readonly groups: readonly { readonly name: string; readonly count: number }[];
  readonly fieldKeys: readonly string[];
}

/** state: invitable, unsubscribed or bounced. */
export type ContactQuery = { readonly search?: string; readonly group?: string; readonly state?: string; readonly offset?: number; readonly limit?: number };

export interface ContactInput extends ParticipantInput {
  readonly groups?: readonly string[];
}

/** The organisation's address book (W35). */
export interface ContactsClient {
  list(query?: ContactQuery): Promise<ContactList>;
  /** 400 email-invalid, email-taken or external-id-taken at the field. */
  create(contact: ContactInput): Promise<string>;
  change(id: string, contact: ContactInput): Promise<void>;
  remove(ids: readonly string[]): Promise<{ removed: number }>;
  group(ids: readonly string[], group: string, leave?: boolean): Promise<{ changed: number }>;
  /** By External ID, then email: known ones get the new details. */
  import(people: readonly ParticipantInput[], group: string | null): Promise<{ added: number; updated: number; invalid: readonly { line: number; text: string }[] }>;
  exportUrl(group?: string): string;
  /** The chosen contacts as CSV. */
  exportChosen(ids: readonly string[]): Promise<{ blob: Blob; fileName: string }>;
  /** A file or a pasted table read as columns. */
  table(source: File | string): Promise<PeopleTable>;
}

export function createContactsClient(options: AppClientOptions): ContactsClient {
  const request = createRequest(options);
  const download = createDownload(options);

  return {
    list: ({ search, group, state, offset, limit } = {}) => {
      const query = new URLSearchParams();
      search?.trim() && query.set("search", search.trim());
      group && query.set("group", group);
      state && query.set("state", state);
      offset && query.set("offset", String(offset));
      limit && query.set("limit", String(limit));
      const text = query.toString();

      return request<ContactList>("GET", `/api/app/contacts${text ? `?${text}` : ""}`);
    },
    create: (contact) => request<{ id: string }>("POST", "/api/app/contacts", contact).then((created) => created.id),
    change: (id, contact) => request("PUT", `/api/app/contacts/${encodeURIComponent(id)}`, contact),
    remove: (ids) => request<{ removed: number }>("POST", "/api/app/contacts/remove", { contactIds: ids }),
    group: (ids, group, leave) => request<{ changed: number }>("POST", "/api/app/contacts/groups", { contactIds: ids, group, leave }),
    import: (people, group) => request("POST", "/api/app/contacts/import", { people, group }),
    table: (source) => {
      if (typeof source === "string") {
        return request<PeopleTable>("POST", "/api/app/contacts/table", { text: source });
      }

      const form = new FormData();
      form.append("file", source);
      return request<PeopleTable>("POST", "/api/app/contacts/table", form);
    },
    exportChosen: (ids) => download("POST", "/api/app/contacts/contacts.csv", { contactIds: ids }),
    exportUrl: (group) => `/api/app/contacts/contacts.csv${group ? `?group=${encodeURIComponent(group)}` : ""}`,
  };
}
