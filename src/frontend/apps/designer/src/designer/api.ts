// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyLook, SurveysClient, SurveySummary, ThemesClient } from "@askwerk/app-client";
import type { SurveyDefinition, ThemeTokens } from "@askwerk/schema";

/** The draft the designer edits, with the last published definition for rename checks. */
export interface SurveyDraft {
  readonly surveyId: string;
  readonly title: string;
  /** Number the draft gets when it is published. */
  readonly versionNumber: number;
  readonly definition: SurveyDefinition;
  readonly published: SurveyDefinition | null;
  /** The survey's theme; null or missing for the organisation's default (AWK-17). */
  readonly themeId?: string | null;
}

/** A theme the survey can use. */
export interface ThemeChoice {
  readonly id: string;
  readonly name: string;
  readonly isDefault: boolean;
  /** Its look, for the gallery (W26). */
  readonly tokens?: ThemeTokens;
}

export interface SurveyLink {
  /** system: the instance's address; domain: a verified domain of the organisation (AWK-7). */
  readonly kind: "system" | "domain";
  readonly hostname: string;
  readonly isPrimary: boolean;
  readonly url: string;
}

export interface PublishedSurvey {
  readonly versionNumber: number;
  readonly links: readonly SurveyLink[];
}

/** What the designer needs from the App API. Failures are ApiError from @askwerk/app-client (with a trace id). */
export interface DesignerApi extends Pick<SurveysClient, "rename" | "duplicate" | "close" | "reopen" | "archive" | "approvals" | "approve" | "withdrawApproval"> {
  /** The survey's name, status and counts for the bar and tabs (AWK-29, W17). */
  summary(surveyId: string): Promise<SurveySummary>;
  loadDraft(surveyId: string): Promise<SurveyDraft>;
  saveDraft(surveyId: string, definition: SurveyDefinition): Promise<void>;
  publish(surveyId: string): Promise<PublishedSurvey>;
  /** The organisation's themes (AWK-17, W10 B). */
  listThemes(): Promise<ThemeChoice[]>;
  /** The survey's theme, or the default with null; saved at once, not part of the draft. */
  setTheme(surveyId: string, themeId: string | null): Promise<void>;
  /** The survey's look (W26): the tokens it shows with, its own or not, and where they started. */
  look(surveyId: string): Promise<SurveyLook<ThemeTokens>>;
  /** Gives the survey its own look, saved at once; 400 with the theme checks' findings. */
  setLook(surveyId: string, tokens: ThemeTokens, lookBase: string | null): Promise<void>;
  /** Saves a look for the whole organisation; 403 for roles other than owner and admin. */
  saveTheme(name: string, tokens: ThemeTokens): Promise<string>;
  /** Stores an image and gives its id. */
  uploadImage(file: File): Promise<string>;
  /** Where the designer shows a stored image; a width asks for a smaller copy. */
  assetUrl(assetId: string, width?: number): string;
  /** Saves the survey's draft as the organisation's template (AWK-17, W10 C). */
  saveAsTemplate(surveyId: string, name: string): Promise<void>;
}

/** The designer's API on top of the App API's survey endpoints (AWK-15 step 3). */
export function designerApi(surveys: SurveysClient, themes: ThemesClient): DesignerApi {
  return {
    summary: (surveyId) => surveys.summary(surveyId),
    rename: (surveyId, title) => surveys.rename(surveyId, title),
    duplicate: (surveyId, title) => surveys.duplicate(surveyId, title),
    close: (surveyId) => surveys.close(surveyId),
    reopen: (surveyId) => surveys.reopen(surveyId),
    archive: (surveyId) => surveys.archive(surveyId),
    approvals: (surveyId) => surveys.approvals(surveyId),
    approve: (surveyId, note) => surveys.approve(surveyId, note),
    withdrawApproval: (surveyId, approvalId) => surveys.withdrawApproval(surveyId, approvalId),
    loadDraft: (surveyId) => surveys.loadDraft<SurveyDefinition>(surveyId),
    saveDraft: (surveyId, definition) => surveys.saveDraft(surveyId, definition),
    publish: (surveyId) => surveys.publish(surveyId),
    listThemes: () => themes.list<ThemeTokens>().then((list) => list.map(({ id, name, isDefault, tokens }) => ({ id, name, isDefault, tokens }))),
    look: (surveyId) => surveys.look<ThemeTokens>(surveyId),
    setLook: (surveyId, tokens, lookBase) => surveys.setLook(surveyId, tokens, lookBase),
    saveTheme: (name, tokens) => themes.create(name, tokens),
    setTheme: (surveyId, themeId) => surveys.setTheme(surveyId, themeId),
    uploadImage: (file) => themes.upload(file, "image").then((stored) => stored.id),
    saveAsTemplate: (surveyId, name) => surveys.saveAsTemplate(surveyId, name),
    assetUrl: (assetId, width) => `${themes.assetUrl(assetId)}${width === undefined ? "" : `?w=${width}`}`,
  };
}
