// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written AuthClient for the account screens' tests: answers are set up per test, calls are recorded.
import { ApiError, type AuthClient, type Me, type Passkey, type Problem, type SessionResponse, type SlugCheck } from "@askwerk/app-client";
import type { PasskeyDevice } from "./passkeys";

type Answer<T> = T | ApiError;

export const traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

export function problem(status: number, details: Problem): ApiError {
  return new ApiError(status, traceId, "failed", details);
}

export const anna: Me = {
  id: "0199e000-0000-7000-8000-0000000000a1",
  name: "Anna Berger",
  email: "anna@acme.at",
  state: "signed-in",
  twoFactorEnabled: true,
  organisations: [{ id: "0199e000-0000-7000-8000-00000000000a", name: "Acme GmbH", slug: "acme", role: "owner" }],
  currentOrganisationId: "0199e000-0000-7000-8000-00000000000a",
  recoveryCodesLeft: 8,
  twoFactorRequired: true,
  language: null,
  theme: "system",
};

export const macBook: Passkey = { id: "bWFjYm9vaw", name: "MacBook · Touch ID", createdAt: "2026-10-13T08:00:00Z", lastUsedAt: "2026-10-14T07:30:00Z" };
export const pixel: Passkey = { id: "cGl4ZWw", name: "Pixel 7", createdAt: "2026-10-13T09:00:00Z", lastUsedAt: null };

/** A passkey authenticator for the screens' tests: answers are set up per test, calls are recorded. */
export class FakePasskeyDevice implements PasskeyDevice {
  readonly calls: [string, unknown][] = [];
  supported = true;
  answer: string | Error | DOMException = '{"id":"bmV3"}';

  create = (options: object) => this.respond("create", options);
  get = (options: object) => this.respond("get", options);

  private respond(name: string, options: object): Promise<string> {
    this.calls.push([name, options]);
    return typeof this.answer === "string" ? Promise.resolve(this.answer) : Promise.reject(this.answer);
  }
}

export class FakeAuthClient implements AuthClient {
  readonly calls: [string, ...unknown[]][] = [];
  signInAnswers: Answer<SessionResponse>[] = [{ state: "signed-in" }];
  codeAnswers: Answer<SessionResponse>[] = [{ state: "signed-in" }];
  setupAnswers: Answer<SessionResponse>[] = [{ state: "two-factor-setup-required" }];
  enableAnswers: Answer<{ state: "signed-in"; recoveryCodes: string[] }>[] = [{ state: "signed-in", recoveryCodes: ["AAAAA-11111", "BBBBB-22222"] }];
  resetConfirmAnswers: Answer<void>[] = [undefined];
  slugAnswer: SlugCheck = { available: true, suggestion: "acme-gmbh" };
  setupNeeded = false;
  account: Me | null = anna;
  resetAvailable = true;
  name: string | null = "Acme Surveys";
  passkeyList: Passkey[] = [macBook, pixel];
  /** Answers for every call that is confirmed with the password (remove passkey, new codes, new phone, turn off). */
  confirmAnswers: Answer<void>[] = [undefined];
  passwordAnswers: Answer<void>[] = [undefined];
  resetTwoFactorAnswer: SessionResponse = { state: "two-factor-setup-required" };
  passkeySignInAnswers: Answer<SessionResponse>[] = [{ state: "signed-in" }];

  setupRequired = () => this.record("setupRequired", this.setupNeeded);
  instanceName = () => this.record("instanceName", this.name);
  checkSlug = (name: string, slug: string) => this.record("checkSlug", this.slugAnswer, name, slug);
  sendTestEmail = (smtp: unknown, to: string, language: string) => this.record("sendTestEmail", undefined, smtp, to, language);
  completeSetup = (request: unknown) => this.next("completeSetup", this.setupAnswers, request);
  signIn = (email: string, password: string) => this.next("signIn", this.signInAnswers, email, password);
  submitTwoFactorCode = (code: string, rememberBrowser: boolean) => this.next("submitTwoFactorCode", this.codeAnswers, code, rememberBrowser);
  submitRecoveryCode = (code: string) => this.next("submitRecoveryCode", this.codeAnswers, code);
  signOut = () => this.record("signOut", undefined);
  me = () => this.record("me", this.account);
  switchOrganisation = (tenantId: string) => this.record("switchOrganisation", { state: "signed-in" as const }, tenantId);
  twoFactorSetup = () =>
    this.record("twoFactorSetup", {
      sharedKey: "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
      authenticatorUri: "otpauth://totp/Acme%20Surveys:anna%40acme.at?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=Acme%20Surveys&digits=6",
    });
  enableTwoFactor = (code: string) => this.next("enableTwoFactor", this.enableAnswers, code);
  passwordResetAvailable = () => this.record("passwordResetAvailable", this.resetAvailable);
  requestPasswordReset = (email: string) => this.record("requestPasswordReset", undefined, email);
  confirmPasswordReset = (email: string, token: string, password: string) => this.next("confirmPasswordReset", this.resetConfirmAnswers, email, token, password);
  changePassword = (currentPassword: string, newPassword: string) => this.next("changePassword", this.passwordAnswers, currentPassword, newPassword);
  newRecoveryCodes = (password: string) => this.confirmed("newRecoveryCodes", ["CCCCC-33333", "DDDDD-44444"], password);
  resetTwoFactor = (password: string) => this.confirmed("resetTwoFactor", this.resetTwoFactorAnswer, password);
  disableTwoFactor = (password: string) => this.confirmed("disableTwoFactor", undefined, password);
  savePreferences = (language: string | null, theme: string) => this.record("savePreferences", undefined, language, theme);
  saveView = (view: "simple" | "advanced") => this.record("saveView", undefined, view);
  signOutEverywhere = () => this.record("signOutEverywhere", undefined);
  passkeys = () => this.record("passkeys", [...this.passkeyList]);
  passkeyCreationOptions = () => this.record("passkeyCreationOptions", { challenge: "Y3JlYXRl" });
  addPasskey = (credential: string, name: string | null) => this.record("addPasskey", { state: "signed-in" as const }, credential, name);
  renamePasskey = (id: string, name: string | null) => this.record("renamePasskey", undefined, id, name);
  removePasskey = (id: string, password: string) => this.confirmed("removePasskey", undefined, id, password);
  passkeyRequestOptions = () => this.record("passkeyRequestOptions", { challenge: "Z2V0" });
  signInWithPasskey = (credential: string) => this.next("signInWithPasskey", this.passkeySignInAnswers, credential);
  ssoAnswers: Answer<{ oidc: boolean; ldap: boolean; oidcName: string | null; ldapName: string | null }>[] = [{ oidc: true, ldap: false, oidcName: "Acme", ldapName: null }];
  directoryAnswers: Answer<SessionResponse>[] = [{ state: "signed-in" }];
  singleSignOnOptions = (organisation: string) => this.next("singleSignOnOptions", this.ssoAnswers, organisation);
  signInWithDirectory = (organisation: string, username: string, password: string) => this.next("signInWithDirectory", this.directoryAnswers, organisation, username, password);

  private async confirmed<T>(name: string, value: T, ...args: unknown[]): Promise<T> {
    await this.next(name, this.confirmAnswers, ...args);
    return value;
  }

  private record<T>(name: string, value: T, ...args: unknown[]): Promise<T> {
    this.calls.push([name, ...args]);
    return Promise.resolve(value);
  }

  private next<T>(name: string, answers: Answer<T>[], ...args: unknown[]): Promise<T> {
    this.calls.push([name, ...args]);
    const answer = answers.length > 1 ? answers.shift()! : answers[0]!;

    return answer instanceof ApiError ? Promise.reject(answer) : Promise.resolve(answer);
  }
}
