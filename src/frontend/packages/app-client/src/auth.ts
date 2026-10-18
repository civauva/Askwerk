// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, createRequest, type AppClientOptions } from "./client";
import type { components } from "./schema";

type Schemas = components["schemas"];

export type SessionState = "signed-in" | "two-factor-required" | "two-factor-setup-required" | "passkey-required";
export type SessionResponse = { state: SessionState };
export type SetupRequest = Schemas["SetupRequest"];
export type SetupEmail = Schemas["SetupEmail"];
export type SlugCheck = Schemas["SlugCheckResponse"];
/** An organisation of the user; suspended ones are closed until an installation administrator resumes them (AWK-38). */
export type Organisation = Omit<Schemas["OrganisationResponse"], "status"> & { status?: string };
/** The signed-in user; installationAdmin: administers this Askwerk (AWK-38). */
export type Me = Omit<Schemas["MeResponse"], "state" | "organisations" | "installationAdmin" | "view"> & { state: SessionState; organisations: Organisation[]; installationAdmin?: boolean; view?: string };
export type TwoFactorSetup = Schemas["TwoFactorSetupResponse"];
export type TwoFactorEnabled = { state: SessionState; recoveryCodes: string[] };
export type Passkey = Schemas["PasskeyResponse"];
export type Theme = "system" | "light" | "dark";

/** Setup, sign-in and the account (AWK-16): the calls behind the sign-in screens. */
/** Single sign-on, the directory, or both, with their names. */
export interface SingleSignOnOptions {
  readonly oidc: boolean;
  readonly ldap: boolean;
  readonly oidcName: string | null;
  readonly ldapName: string | null;
}

export interface AuthClient {
  setupRequired(): Promise<boolean>;
  instanceName(): Promise<string | null>;
  checkSlug(name: string, slug: string): Promise<SlugCheck>;
  sendTestEmail(smtp: SetupEmail, to: string, language: string): Promise<void>;
  completeSetup(request: SetupRequest): Promise<SessionResponse>;
  signIn(email: string, password: string): Promise<SessionResponse>;
  submitTwoFactorCode(code: string, rememberBrowser: boolean): Promise<SessionResponse>;
  submitRecoveryCode(code: string): Promise<SessionResponse>;
  signOut(): Promise<void>;
  me(): Promise<Me | null>;
  switchOrganisation(tenantId: string): Promise<SessionResponse>;
  twoFactorSetup(): Promise<TwoFactorSetup>;
  enableTwoFactor(code: string): Promise<TwoFactorEnabled>;
  passwordResetAvailable(): Promise<boolean>;
  requestPasswordReset(email: string): Promise<void>;
  confirmPasswordReset(email: string, token: string, password: string): Promise<void>;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;
  newRecoveryCodes(password: string): Promise<string[]>;
  resetTwoFactor(password: string): Promise<SessionResponse>;
  disableTwoFactor(password: string): Promise<void>;
  savePreferences(language: string | null, theme: string): Promise<void>;
  /** Simple or Advanced (W45, AWK-71): what the app shows this person. */
  saveView(view: "simple" | "advanced"): Promise<void>;
  signOutEverywhere(): Promise<void>;
  passkeys(): Promise<Passkey[]>;
  /** Options for navigator.credentials.create(), as JSON (PublicKeyCredential.parseCreationOptionsFromJSON). */
  passkeyCreationOptions(): Promise<object>;
  /** Adds the credential, given as the JSON of PublicKeyCredential.toJSON(). */
  addPasskey(credential: string, name: string | null): Promise<SessionResponse>;
  renamePasskey(id: string, name: string | null): Promise<void>;
  removePasskey(id: string, password: string): Promise<void>;
  /** Options for navigator.credentials.get(), as JSON (PublicKeyCredential.parseRequestOptionsFromJSON). */
  passkeyRequestOptions(): Promise<object>;
  signInWithPasskey(credential: string): Promise<SessionResponse>;
  /** How an organisation's people sign in besides passwords (AWK-12); 404 ApiError when it has neither way. */
  singleSignOnOptions(organisation: string): Promise<SingleSignOnOptions>;
  /** Signs in with the directory's user name and password; problems: invalid-credentials, no-access, no-email, locked, directory-unreachable. */
  signInWithDirectory(organisation: string, username: string, password: string): Promise<SessionResponse>;
}

export function createAuthClient(options: AppClientOptions): AuthClient {
  const request = createRequest(options);

  return {
    setupRequired: () => request<{ required: boolean }>("GET", "/api/app/setup").then((setup) => setup.required, notFoundAs(false)),
    instanceName: () => request<{ name: string | null }>("GET", "/api/app/instance").then((instance) => instance.name),
    checkSlug: (name, slug) => request<SlugCheck>("GET", `/api/app/setup/slug?name=${encodeURIComponent(name)}&slug=${encodeURIComponent(slug)}`),
    sendTestEmail: (smtp, to, language) => request("POST", "/api/app/setup/test-email", { smtp, to, language }),
    completeSetup: (body) => request<SessionResponse>("POST", "/api/app/setup", body),
    signIn: (email, password) => request<SessionResponse>("POST", "/api/app/session", { email, password }),
    submitTwoFactorCode: (code, rememberBrowser) => request<SessionResponse>("POST", "/api/app/session/two-factor", { code, rememberBrowser }),
    submitRecoveryCode: (code) => request<SessionResponse>("POST", "/api/app/session/recovery", { code }),
    signOut: () => request("DELETE", "/api/app/session"),
    me: () => request<Me>("GET", "/api/app/me").catch(statusAs(401, null)),
    switchOrganisation: (tenantId) => request<SessionResponse>("PUT", "/api/app/session/tenant", { tenantId }),
    twoFactorSetup: () => request<TwoFactorSetup>("GET", "/api/app/account/two-factor"),
    enableTwoFactor: (code) => request<TwoFactorEnabled>("POST", "/api/app/account/two-factor", { code }),
    passwordResetAvailable: () => request<{ available: boolean }>("GET", "/api/app/password-reset").then((state) => state.available),
    requestPasswordReset: (email) => request("POST", "/api/app/password-reset", { email }),
    confirmPasswordReset: (email, token, password) => request("POST", "/api/app/password-reset/confirm", { email, token, password }),
    changePassword: (currentPassword, newPassword) => request("POST", "/api/app/account/password", { currentPassword, newPassword }),
    newRecoveryCodes: (password) =>
      request<{ recoveryCodes: string[] }>("POST", "/api/app/account/two-factor/recovery-codes", { password }).then((codes) => codes.recoveryCodes),
    resetTwoFactor: (password) => request<SessionResponse>("POST", "/api/app/account/two-factor/reset", { password }),
    disableTwoFactor: (password) => request("POST", "/api/app/account/two-factor/disable", { password }),
    savePreferences: (language, theme) => request("PUT", "/api/app/account/preferences", { language, theme }),
    saveView: (view) => request("PUT", "/api/app/account/view", { view }),
    signOutEverywhere: () => request("POST", "/api/app/account/sign-out-everywhere"),
    passkeys: () => request<Passkey[]>("GET", "/api/app/account/passkeys"),
    passkeyCreationOptions: () => request<object>("POST", "/api/app/account/passkeys/options"),
    addPasskey: (credential, name) => request<SessionResponse>("POST", "/api/app/account/passkeys", { credential, name }),
    renamePasskey: (id, name) => request("PUT", `/api/app/account/passkeys/${encodeURIComponent(id)}`, { name }),
    removePasskey: (id, password) => request("POST", `/api/app/account/passkeys/${encodeURIComponent(id)}/remove`, { password }),
    passkeyRequestOptions: () => request<object>("POST", "/api/app/session/passkey/options"),
    signInWithPasskey: (credential) => request<SessionResponse>("POST", "/api/app/session/passkey", { credential }),
    singleSignOnOptions: (organisation) => request<SingleSignOnOptions>("GET", `/api/app/session/sso/${encodeURIComponent(organisation)}`),
    signInWithDirectory: (organisation, username, password) => request<SessionResponse>("POST", "/api/app/session/ldap", { organisation, username, password }),
  };
}

function notFoundAs<T>(value: T) {
  return statusAs(404, value);
}

function statusAs<T>(status: number, value: T) {
  return (error: unknown): T => {
    if (error instanceof ApiError && error.status === status) {
      return value;
    }

    throw error;
  };
}
