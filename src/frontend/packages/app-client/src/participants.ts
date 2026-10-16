// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

/** A person to invite. */
export interface ParticipantInput {
  readonly email: string;
  readonly name: string | null;
  readonly language: string | null;
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
}

/** One page of the participants (W13 C); total counts all of the survey's. */
export interface ParticipantList {
  readonly items: readonly ParticipantItem[];
  readonly total: number;
  readonly counts: Readonly<Partial<Record<ParticipantStatus, number>>>;
  readonly reminderAt: string | null;
}

/** sort: name, status or sent; a leading minus sorts descending (newest first by default). */
export type ParticipantQuery = { readonly sort?: string; readonly offset?: number; readonly limit?: number };

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
}

export function createParticipantsClient(options: AppClientOptions): ParticipantsClient {
  const request = createRequest(options);
  const at = (surveyId: string) => `/api/app/surveys/${encodeURIComponent(surveyId)}`;

  return {
    checkText: (surveyId, text) => request<ParticipantCheck>("POST", `${at(surveyId)}/participants/check`, { text }),
    checkFile: (surveyId, file) => {
      const form = new FormData();
      form.append("file", file);
      return request<ParticipantCheck>("POST", `${at(surveyId)}/participants/check`, form);
    },
    invite: (surveyId, body) => request<{ invited: number; skipped: number }>("POST", `${at(surveyId)}/invitations`, body),
    list: (surveyId, { sort, offset, limit } = {}) => {
      const query = new URLSearchParams();
      sort && query.set("sort", sort);
      offset && query.set("offset", String(offset));
      limit && query.set("limit", String(limit));
      const text = query.toString();

      return request<ParticipantList>("GET", `${at(surveyId)}/participants${text ? `?${text}` : ""}`);
    },
    remind: (surveyId, selection) => request("POST", `${at(surveyId)}/participants/remind`, selection),
    remove: (surveyId, selection) => request<{ removed: number; kept: number }>("POST", `${at(surveyId)}/participants/remove`, selection),
    linksUrl: (surveyId) => `${at(surveyId)}/participants/links.csv`,
  };
}
