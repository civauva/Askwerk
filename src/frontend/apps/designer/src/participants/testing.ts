// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written ParticipantsClient for the invite dialog's and the participants list's tests.
import { ApiError, type InviteRequest, type ParticipantCheck, type ParticipantList, type ParticipantQuery, type ParticipantsClient, type ParticipantSelection } from "@askwerk/app-client";

export const sampleList: ParticipantList = {
  items: [
    { id: "p1", email: "anna@acme.at", name: "Anna Berger", language: "de", status: "answered", sentAt: "2026-10-14T08:00:00Z", reminders: 0, link: "https://surveys.acme.at/s/kz26b7mq?t=aaa" },
    { id: "p2", email: "josef@acme.at", name: "Josef Huber", language: null, status: "started", sentAt: "2026-10-14T08:00:00Z", reminders: 1, link: "https://surveys.acme.at/s/kz26b7mq?t=bbb" },
    { id: "p3", email: "old@acme.at", name: null, language: null, status: "bounced", sentAt: null, reminders: 0, link: "https://surveys.acme.at/s/kz26b7mq?t=ccc" },
  ],
  total: 3,
  counts: { answered: 1, started: 1, bounced: 1 },
  reminderAt: "2026-10-25T08:00:00Z",
};

export class FakeParticipantsClient implements ParticipantsClient {
  readonly calls: [string, ...unknown[]][] = [];
  emailReady = true;
  /** How many the survey has, so a test can page through more than the three shown. */
  total = sampleList.total;
  list = (surveyId: string, query?: ParticipantQuery) => {
    this.calls.push(query === undefined ? ["list", surveyId] : ["list", surveyId, query]);
    return Promise.resolve({ ...sampleList, total: this.total });
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
}
