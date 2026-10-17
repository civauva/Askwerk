// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

export type DomainStatus = "pending" | "verified" | "failed";

/** An organisation's own domain (AWK-7): the two DNS records to set, and what the last check found. */
export interface DomainItem {
  readonly id: string;
  readonly hostname: string;
  readonly status: DomainStatus;
  readonly isPrimary: boolean;
  readonly cnameTarget: string;
  readonly txtName: string;
  readonly txtValue: string;
  readonly cnameFound: boolean;
  readonly txtFound: boolean;
  readonly createdAt: string;
  readonly checkedAt: string | null;
  readonly verifiedAt: string | null;
}

export interface DomainList {
  /** The instance's own address; primary while no domain is. */
  readonly system: { readonly hostname: string; readonly isPrimary: boolean };
  readonly items: readonly DomainItem[];
}

/** Settings → Domains (AWK-7, W12 A). */
export interface DomainsClient {
  list(): Promise<DomainList>;
  /** 400 with errors.hostname, or 409 domain-taken. */
  add(hostname: string): Promise<DomainItem>;
  verify(id: string): Promise<DomainItem>;
  /** A verified domain, or null for the system address. */
  setPrimary(id: string | null): Promise<void>;
  remove(id: string): Promise<void>;
  /** The websites that may show the surveys in a frame; none switches embedding off. */
  embedding(): Promise<string[]>;
  /** 400 with errors.origins: origin-invalid or too-many-origins. */
  setEmbedding(origins: readonly string[]): Promise<void>;
  /** The websites that may show the designer and Results (AWK-13); none switches it off. */
  designerEmbedding(): Promise<string[]>;
  setDesignerEmbedding(origins: readonly string[]): Promise<void>;
  /** The host look drop-in components follow (W37); null when not set. */
  hostLook(): Promise<HostLookSettings | null>;
  /** Keeps the host look; all null clears it. 400 look-invalid. */
  setHostLook(look: HostLookSettings): Promise<HostLookSettings>;
  /** A survey's own websites, some of the organisation's; none uses them all (W21 3). */
  surveyEmbedding(surveyId: string): Promise<SurveyEmbedding>;
  setSurveyEmbedding(surveyId: string, origins: readonly string[]): Promise<void>;
}

export function createDomainsClient(options: AppClientOptions): DomainsClient {
  const request = createRequest(options);
  const at = (id: string) => `/api/app/domains/${encodeURIComponent(id)}`;

  return {
    list: () => request<DomainList>("GET", "/api/app/domains"),
    add: (hostname) => request<DomainItem>("POST", "/api/app/domains", { hostname }),
    verify: (id) => request<DomainItem>("POST", `${at(id)}/verify`),
    setPrimary: (id) => request("PUT", "/api/app/domains/primary", { domainId: id }),
    remove: (id) => request("DELETE", at(id)),
    embedding: () => request<{ origins: string[] }>("GET", "/api/app/embedding").then((answer) => answer.origins),
    setEmbedding: (origins) => request("PUT", "/api/app/embedding", { origins }),
    hostLook: () => request<{ hostLook?: HostLookSettings | null }>("GET", "/api/app/embedding").then((answer) => answer.hostLook ?? null),
    setHostLook: (look) => request<HostLookSettings>("PUT", "/api/app/embedding/host-look", look),
    designerEmbedding: () => request<{ designerOrigins: string[] }>("GET", "/api/app/embedding").then((answer) => answer.designerOrigins),
    setDesignerEmbedding: (origins) => request("PUT", "/api/app/embedding", { designerOrigins: origins }),
    surveyEmbedding: (surveyId) => request<SurveyEmbedding>("GET", `/api/app/surveys/${encodeURIComponent(surveyId)}/embed-origins`),
    setSurveyEmbedding: (surveyId, origins) => request("PUT", `/api/app/surveys/${encodeURIComponent(surveyId)}/embed-origins`, { origins }),
  };
}

/** A survey's websites and the organisation's to choose from. */
export interface SurveyEmbedding {
  readonly origins: string[];
  readonly organisation: string[];
}

/** What an embedded session shows: the designer, Results, the survey list or one key result (W37). */
export type EmbedView = "designer" | "results" | "list" | "result";

/** An embedded session of the designer or Results (AWK-13): its bearer token stays in memory. */
export interface EmbedSession {
  readonly token: string;
  readonly expiresAt: string;
  readonly endsAt: string;
  readonly surveyId: string;
  readonly view: EmbedView;
  readonly language: string | null;
}

/** Starts an embedded session from the host's token; failures are ApiError with code token-invalid, embedding-off, no-access or survey-not-found. */
export function startEmbedSession(options: AppClientOptions, token: string, view: EmbedView): Promise<EmbedSession> {
  return createRequest(options)<EmbedSession>("POST", "/api/app/embed/session", { token, view });
}

/** Renews an embedded session's token for another hour, never past its end. */
export function renewEmbedSession(options: AppClientOptions): Promise<{ expiresAt: string; endsAt: string }> {
  return createRequest(options)<{ expiresAt: string; endsAt: string }>("POST", "/api/app/embed/session/renew");
}

/** One survey's key result (W37, <askwerk-result>): the question by its variable, else the key question. */
export interface SurveyKeyResult {
  readonly title: string;
  readonly responses: number;
  readonly complete: number;
  readonly key: import("./surveys").KeyResult | null;
}

export function getKeyResult(options: AppClientOptions, surveyId: string, question?: string | null): Promise<SurveyKeyResult> {
  return createRequest(options)<SurveyKeyResult>("GET", `/api/app/surveys/${encodeURIComponent(surveyId)}/key-result${question ? `?question=${encodeURIComponent(question)}` : ""}`);
}

/** The host look (W37): accent, background, font and corners in px; null where not set. */
export interface HostLookSettings {
  accent: string | null;
  background: string | null;
  font: string | null;
  radius: number | null;
}
