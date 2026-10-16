// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";
import type { components } from "./schema";

type Schemas = components["schemas"];

export type OrganisationDetails = Schemas["OrganisationDetails"];
export type InstallationInfo = Schemas["InstallationResponse"];
export type InstallationOrganisation = Omit<Schemas["InstallationOrganisation"], "status"> & { status: "active" | "suspended" | "deleted" };
export type RemovalCounts = Schemas["RemovalCounts"];
export type InstallationChange = Schemas["ChangeInstallationRequest"];
export type InstallationEmail = Schemas["InstallationEmailRequest"];
export type InstallationOrganisations = Omit<Schemas["InstallationOrganisationList"], "items"> & { items: InstallationOrganisation[] };
export type CreatedOrganisation = Schemas["CreatedOrganisationResponse"];
export type InstallationAdministrator = Schemas["InstallationAdministrator"];

/** Administration → Organisation (AWK-38): the organisation chosen in the session. */
export interface OrganisationClient {
  get(): Promise<OrganisationDetails>;
  /** 400 at the field: name-required, slug-invalid, slug-taken. */
  change(name: string, slug: string): Promise<void>;
  transfer(userId: string): Promise<void>;
  remove(): Promise<void>;
}

/** Administration → This Askwerk (AWK-38): installation administrators only, 403 for everyone else. */
export interface InstallationClient {
  info(): Promise<InstallationInfo>;
  organisations(): Promise<InstallationOrganisations>;
  createOrganisation(name: string, slug: string, ownerEmail: string): Promise<CreatedOrganisation>;
  suspend(organisationId: string): Promise<void>;
  resume(organisationId: string): Promise<void>;
  deleteOrganisation(organisationId: string): Promise<void>;
  /** Opens a deleted organisation again while its data is there (AWK-39). */
  restore(organisationId: string): Promise<void>;
  /** What removing a deleted organisation's data takes away (AWK-39). */
  removal(organisationId: string): Promise<RemovalCounts>;
  /** Removes a deleted organisation's data now; its short name confirms it (AWK-39). */
  removeData(organisationId: string, slug: string): Promise<void>;
  /** Saves the installation's name, defaults and email (AWK-39). */
  change(settings: InstallationChange): Promise<void>;
  /** Sends a test email to the signed-in administrator; resolves to the address (AWK-39). */
  testEmail(email: InstallationEmail): Promise<string>;
  administrators(): Promise<InstallationAdministrator[]>;
  /** 404 user-not-found. */
  addAdministrator(email: string): Promise<void>;
  /** 409 last-admin. */
  removeAdministrator(userId: string): Promise<void>;
}

export function createOrganisationClient(options: AppClientOptions): OrganisationClient {
  const request = createRequest(options);

  return {
    get: () => request<OrganisationDetails>("GET", "/api/app/organisation"),
    change: (name, slug) => request("PUT", "/api/app/organisation", { name, slug }),
    transfer: (userId) => request("POST", "/api/app/organisation/transfer", { userId }),
    remove: () => request("DELETE", "/api/app/organisation"),
  };
}

export function createInstallationClient(options: AppClientOptions): InstallationClient {
  const request = createRequest(options);
  const at = (id: string) => `/api/app/installation/organisations/${encodeURIComponent(id)}`;

  return {
    info: () => request<InstallationInfo>("GET", "/api/app/installation"),
    organisations: () => request<InstallationOrganisations>("GET", "/api/app/installation/organisations"),
    createOrganisation: (name, slug, ownerEmail) => request<CreatedOrganisation>("POST", "/api/app/installation/organisations", { name, slug, ownerEmail }),
    suspend: (id) => request("POST", `${at(id)}/suspend`),
    resume: (id) => request("POST", `${at(id)}/resume`),
    deleteOrganisation: (id) => request("DELETE", at(id)),
    restore: (id) => request("POST", `${at(id)}/restore`),
    removal: (id) => request<RemovalCounts>("GET", `${at(id)}/removal`),
    removeData: (id, slug) => request("POST", `${at(id)}/remove`, { slug }),
    change: (settings) => request("PUT", "/api/app/installation", settings),
    testEmail: (email) => request<{ to: string }>("POST", "/api/app/installation/test-email", email).then((answer) => answer.to),
    administrators: () => request<{ items: InstallationAdministrator[] }>("GET", "/api/app/installation/administrators").then((answer) => answer.items),
    addAdministrator: (email) => request("POST", "/api/app/installation/administrators", { email }),
    removeAdministrator: (userId) => request("DELETE", `/api/app/installation/administrators/${encodeURIComponent(userId)}`),
  };
}
