// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";
import type { components } from "./schema";

type Schemas = components["schemas"];

export type SurveyStatus = "draft" | "published" | "closed" | "archived";
/** The front page a card draws (W32): the welcome page's title and text, in the look's tokens. */
export type SurveyFront<TTokens = unknown> = { title: Record<string, string> | null; text: Record<string, string> | null; defaultLanguage: string; look: TTokens; headerImage?: string | null };
export type SurveyListItem = Omit<Schemas["SurveyListItem"], "status" | "front"> & { status: SurveyStatus; front?: SurveyFront | null };
/** One page of the survey list (W13 A); total counts every survey that matches the status and search. */
export type SurveyList = { items: SurveyListItem[]; counts: Record<"all" | "draft" | "published" | "closed", number>; total: number };
/** sort: title, status, responses or changed; a leading minus sorts descending (-changed by default). */
export type SurveyListQuery = { status?: SurveyStatus; search?: string; sort?: string; offset?: number; limit?: number; cards?: boolean };
export type NewSurvey = { title: string; languages: string[]; defaultLanguage: string; templateId?: string };
/** A template to start a survey from (AWK-17): Askwerk's have names per language, the organisation's one name. */
export type SurveyTemplate = {
  id: string;
  scope: "askwerk" | "organisation";
  name: string | null;
  names: Record<string, string> | null;
  questions: number;
  languages: string[];
  /** The organisation's, once edited: the survey the designer edits it in (W32). */
  surveyId?: string | null;
  /** The front page a card draws (W32). */
  front?: SurveyFront | null;
};
/** A template opened for editing (W32): the survey the designer edits it in. */
export type TemplateEdit = { id: string; surveyId: string };
/** A survey's look (W26): the tokens it is shown with, whether they are its own, and the look they started from. */
export type SurveyLook<TTokens = unknown> = { tokens: TTokens; own: boolean; lookBase: string | null; themeId: string | null };
/** One of Askwerk's sample surveys (W26), with its look's tokens and a question to draw its card. */
export type SampleSurvey<TTokens = unknown> = {
  id: string;
  names: Record<string, string>;
  kind: Record<string, string>;
  features: Record<string, string[]>;
  look: string;
  tokens: TTokens;
  languages: string[];
  questions: number;
  responses: number;
  preview: { question: Record<string, string>; choices: Record<string, string>[] };
};
/** The definition stays opaque here; packages/schema types it. */
export type SurveyDraftResponse<TDefinition = unknown> = {
  surveyId: string;
  title: string;
  versionNumber: number;
  definition: TDefinition;
  published: TDefinition | null;
  /** The survey's theme; null for the organisation's default (AWK-17). */
  themeId?: string | null;
};
/** A link to a survey (AWK-7): on the system address or a verified domain of the organisation. */
export type SurveyLink = { kind: "system" | "domain"; hostname: string; isPrimary: boolean; url: string };
export type PublishedSurvey = { versionNumber: number; links: SurveyLink[] };

/** What a survey's bar and tabs show (AWK-29). */
export type SurveySummary = {
  id: string;
  title: string;
  status: SurveyStatus;
  publishedVersion: number | null;
  hasChanges: boolean;
  languages: string[];
  responses: number;
  participants: number;
  /** The closing date (W24), ISO 8601 in UTC; null for none. */
  closesAt?: string | null;
  /** The instance's time zone the closing date is entered and shown in, e.g. Europe/Vienna. */
  timeZone?: string;
  /** Whether a browser that finished may not answer again (W24); on when absent. */
  oneResponsePerPerson?: boolean;
  /** Set when the survey is where a template is edited (W32): no publishing, no results. */
  templateId?: string | null;
};

/** One approval of a survey's draft (AWK-28, W16). */
export type SurveyApproval = {
  id: string;
  by: string;
  at: string;
  note: string | null;
  fingerprint: string;
  /** Number of the draft it was given for. */
  version: number;
  /** It approves the current draft. */
  current: boolean;
  /** The signed-in user gave it and may withdraw it. */
  mine: boolean;
};

/** Every approval of a survey, newest first; approved: this draft, earlier: an earlier draft only. */
export type SurveyApprovals = { fingerprint: string; state: "approved" | "earlier" | "none"; items: SurveyApproval[] };

/** Surveys of the current organisation (AWK-15 step 3, W6): the list, new surveys, and what the designer needs. */
export interface SurveysClient {
  list(query: SurveyListQuery): Promise<SurveyList>;
  create(survey: NewSurvey): Promise<string>;
  loadDraft<TDefinition = unknown>(surveyId: string): Promise<SurveyDraftResponse<TDefinition>>;
  saveDraft(surveyId: string, definition: unknown): Promise<void>;
  publish(surveyId: string): Promise<PublishedSurvey>;
  /** Chooses the survey's theme, or the organisation's default with null (AWK-17). */
  setTheme(surveyId: string, themeId: string | null): Promise<void>;
  /** The survey's look (W26). */
  look<TTokens = unknown>(surveyId: string): Promise<SurveyLook<TTokens>>;
  /** Gives the survey its own look; 400 theme-checks-failed with the issues (W26). */
  setLook(surveyId: string, tokens: unknown, lookBase: string | null): Promise<void>;
  /** Drops the survey's own look: its theme or the organisation's default again (W26). */
  dropLook(surveyId: string): Promise<void>;
  /** Askwerk's sample surveys (W26). */
  listSamples<TTokens = unknown>(): Promise<SampleSurvey<TTokens>[]>;
  /** Creates a sample, closed with its sample answers; returns the survey's id (W26). */
  createSample(sampleId: string, title: string): Promise<string>;
  /** Askwerk's templates, then the organisation's (AWK-17). */
  listTemplates(): Promise<SurveyTemplate[]>;
  /** Saves the survey's draft as the organisation's template. */
  saveAsTemplate(surveyId: string, name: string): Promise<void>;
  deleteTemplate(templateId: string): Promise<void>;
  /** Opens one of the organisation's templates in the designer (W32). */
  editTemplate(templateId: string): Promise<TemplateEdit>;
  /** Copies one of Askwerk's templates to the organisation's, ready to edit (W32). */
  copyTemplate(templateId: string, language: string): Promise<TemplateEdit>;
  /** The bar's status and the tabs' counts (AWK-29); every member reads it. */
  summary(surveyId: string): Promise<SurveySummary>;
  /** The survey's menu (W17 D); editors only, 403 role-not-allowed otherwise. */
  rename(surveyId: string, title: string): Promise<void>;
  /** Copies the draft into a new survey; returns its id. */
  duplicate(surveyId: string, title: string): Promise<string>;
  /** Stops responses; 409 not-published before publishing. */
  close(surveyId: string): Promise<void>;
  reopen(surveyId: string): Promise<void>;
  archive(surveyId: string): Promise<void>;
  /** Sets the closing date as a local date and time in the instance's time zone (2026-10-31T18:00), or removes it with null; 400 closing-date-past (W24). */
  setClosingDate(surveyId: string, closesAt: string | null): Promise<void>;
  /** One response per person on or off (W24). */
  setOneResponse(surveyId: string, on: boolean): Promise<void>;
  /** The survey's links, primary first (the Share tab, AWK-29). */
  links(surveyId: string): Promise<SurveyLink[]>;
  /** Approvals of the draft (AWK-28); every member may approve. */
  approvals(surveyId: string): Promise<SurveyApprovals>;
  /** Approves the saved draft; returns the approval's id. */
  approve(surveyId: string, note: string | null): Promise<string>;
  /** Withdraws one's own approval; 403 for someone else's. */
  withdrawApproval(surveyId: string, approvalId: string): Promise<void>;
}

export function createSurveysClient(options: AppClientOptions): SurveysClient {
  const request = createRequest(options);
  const at = (surveyId: string) => `/api/app/surveys/${encodeURIComponent(surveyId)}`;

  return {
    list: ({ status, search, sort, offset, limit, cards }) => {
      const query = new URLSearchParams();
      status && query.set("status", status);
      search?.trim() && query.set("search", search.trim());
      sort && query.set("sort", sort);
      offset && query.set("offset", String(offset));
      limit && query.set("limit", String(limit));
      cards && query.set("cards", "true");
      const text = query.toString();

      return request<SurveyList>("GET", `/api/app/surveys${text ? `?${text}` : ""}`);
    },
    create: (survey) => request<{ id: string }>("POST", "/api/app/surveys", survey).then((created) => created.id),
    loadDraft: <TDefinition>(surveyId: string) => request<SurveyDraftResponse<TDefinition>>("GET", `${at(surveyId)}/draft`),
    saveDraft: (surveyId, definition) => request("PUT", `${at(surveyId)}/draft`, { definition }),
    publish: (surveyId) => request<PublishedSurvey>("POST", `${at(surveyId)}/publish`),
    setTheme: (surveyId, themeId) => request("PUT", `${at(surveyId)}/theme`, { themeId }),
    look: <TTokens>(surveyId: string) => request<SurveyLook<TTokens>>("GET", `${at(surveyId)}/look`),
    setLook: (surveyId, tokens, lookBase) => request("PUT", `${at(surveyId)}/look`, { tokens, lookBase }),
    dropLook: (surveyId) => request("DELETE", `${at(surveyId)}/look`),
    listSamples: <TTokens>() => request<{ items: SampleSurvey<TTokens>[] }>("GET", "/api/app/samples").then((answer) => answer.items),
    createSample: (sampleId, title) => request<{ id: string }>("POST", `/api/app/samples/${encodeURIComponent(sampleId)}`, { title }).then((created) => created.id),
    listTemplates: () => request<{ items: SurveyTemplate[] }>("GET", "/api/app/templates").then((answer) => answer.items),
    saveAsTemplate: (surveyId, name) => request<{ id: string }>("POST", "/api/app/templates", { surveyId, name }).then(() => undefined),
    deleteTemplate: (templateId) => request("DELETE", `/api/app/templates/${encodeURIComponent(templateId)}`),
    editTemplate: (templateId) => request<TemplateEdit>("POST", `/api/app/templates/${encodeURIComponent(templateId)}/edit`),
    copyTemplate: (templateId, language) => request<TemplateEdit>("POST", `/api/app/templates/${encodeURIComponent(templateId)}/copy`, { language }),
    summary: (surveyId) => request<SurveySummary>("GET", `${at(surveyId)}/summary`),
    rename: (surveyId, title) => request("PUT", `${at(surveyId)}/title`, { title }),
    duplicate: (surveyId, title) => request<{ id: string }>("POST", `${at(surveyId)}/duplicate`, { title }).then((created) => created.id),
    close: (surveyId) => request("POST", `${at(surveyId)}/close`),
    reopen: (surveyId) => request("POST", `${at(surveyId)}/reopen`),
    archive: (surveyId) => request("POST", `${at(surveyId)}/archive`),
    setClosingDate: (surveyId, closesAt) => request("PUT", `${at(surveyId)}/closing-date`, { closesAt }),
    setOneResponse: (surveyId, on) => request("PUT", `${at(surveyId)}/one-response`, { on }),
    approvals: (surveyId) => request<SurveyApprovals>("GET", `${at(surveyId)}/approvals`),
    approve: (surveyId, note) => request<{ id: string }>("POST", `${at(surveyId)}/approvals`, { note }).then((created) => created.id),
    withdrawApproval: (surveyId, approvalId) => request("DELETE", `${at(surveyId)}/approvals/${encodeURIComponent(approvalId)}`),
    links: (surveyId) => request<{ code: string; links: SurveyLink[] }>("GET", `${at(surveyId)}/links`).then((answer) => answer.links),
  };
}
