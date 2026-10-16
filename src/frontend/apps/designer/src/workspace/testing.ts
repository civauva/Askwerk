// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyApprovals, SurveyLink, SurveySummary } from "@askwerk/app-client";
import type { WorkspaceSurveys } from "./SurveyFrame";

export const publishedSummary: SurveySummary = {
  id: "s1",
  title: "Customer satisfaction 2026",
  status: "published",
  publishedVersion: 3,
  hasChanges: false,
  languages: ["de", "en"],
  responses: 248,
  participants: 640,
};

export const sampleLinks: SurveyLink[] = [
  { kind: "domain", hostname: "surveys.acme.at", isPrimary: true, url: "https://surveys.acme.at/s/kz26b7mq" },
  { kind: "system", hostname: "acme.askwerk.app", isPrimary: false, url: "https://acme.askwerk.app/s/kz26b7mq" },
];

/** The survey endpoints the workspace uses, in memory. */
export class FakeWorkspaceSurveys implements WorkspaceSurveys {
  readonly calls: string[] = [];

  constructor(
    public answer: SurveySummary = publishedSummary,
    public linkList: SurveyLink[] = sampleLinks,
  ) {}

  approvalAnswer: SurveyApprovals = { fingerprint: "7f3a91c2", state: "none", items: [] };

  approvals(): Promise<SurveyApprovals> {
    return Promise.resolve(this.approvalAnswer);
  }

  approve(_surveyId: string, note: string | null): Promise<string> {
    this.calls.push(`approve ${note ?? ""}`.trim());
    this.approvalAnswer = {
      ...this.approvalAnswer,
      state: "approved",
      items: [{ id: "a1", by: "Ben Huber", at: new Date().toISOString(), note, fingerprint: "7f3a91c2", version: 1, current: true, mine: true }, ...this.approvalAnswer.items],
    };
    return Promise.resolve("a1");
  }

  withdrawApproval(): Promise<void> {
    this.calls.push("withdraw");
    return Promise.resolve();
  }

  summary(): Promise<SurveySummary> {
    return Promise.resolve(this.answer);
  }

  links(): Promise<SurveyLink[]> {
    return Promise.resolve(this.linkList);
  }

  rename(_surveyId: string, title: string): Promise<void> {
    this.calls.push(`rename ${title}`);
    this.answer = { ...this.answer, title };
    return Promise.resolve();
  }

  duplicate(): Promise<string> {
    this.calls.push("duplicate");
    return Promise.resolve("s2");
  }

  close(): Promise<void> {
    this.calls.push("close");
    return Promise.resolve();
  }

  reopen(): Promise<void> {
    this.calls.push("reopen");
    return Promise.resolve();
  }

  setClosingDate(_surveyId: string, closesAt: string | null): Promise<void> {
    this.calls.push(`closing-date ${closesAt ?? "none"}`);
    return Promise.resolve();
  }

  setOneResponse(_surveyId: string, on: boolean): Promise<void> {
    this.calls.push(`one-response ${on ? "on" : "off"}`);
    return Promise.resolve();
  }

  archive(): Promise<void> {
    this.calls.push("archive");
    return Promise.resolve();
  }
}
