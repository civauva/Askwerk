// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

export type MemberRole = "owner" | "admin" | "editor" | "analyst" | "viewer";

/** A member of the organisation (AWK-30, W18 A). */
export interface Member {
  readonly id: string;
  readonly name: string;
  readonly email: string;
  readonly role: MemberRole;
  /** App code or passkey set up. */
  readonly twoFactor: boolean;
  readonly lastSignInAt: string | null;
  /** The signed-in user. */
  readonly you: boolean;
  /** The host system that manages this member (W22); locked here when set. */
  readonly managedBy?: string | null;
}

/** An invitation into the organisation; its link works once, for 7 days. */
export interface MemberInvitation {
  readonly id: string;
  readonly email: string;
  readonly role: MemberRole;
  readonly invitedBy: string;
  readonly invitedAt: string;
  readonly expiresAt: string;
  /** When the email went out; null for a link only. */
  readonly sentAt: string | null;
  readonly open: boolean;
}

export interface MembersPage {
  readonly members: Member[];
  readonly invitations: MemberInvitation[];
  /** The signed-in user is an owner and may manage owners. */
  readonly canManageOwners: boolean;
  /** Invitations go out by email; otherwise the links are copied. */
  readonly emailReady: boolean;
}

export interface Invited {
  readonly invitations: { id: string; email: string; link: string }[];
  /** Addresses that are members already; not invited. */
  readonly alreadyMembers: string[];
  /** Every email went out. */
  readonly sent: boolean;
}

/** What an invitation link offers (W18 C). */
export interface InvitationToJoin {
  readonly organisation: string;
  readonly role: MemberRole;
  readonly invitedBy: string;
  readonly email: string;
  /** The address has an account: the person signs in to join. */
  readonly accountExists: boolean;
  /** The account signed in in this browser, if any. */
  readonly signedInAs: string | null;
  readonly expiresAt: string;
}

/**
 * Members of the organisation (AWK-30): owners and admins only, 403 otherwise; 409 with code last-owner or own-role.
 * Joining works without a session: 410 invitation-invalid when the link no longer works, 409 account-exists.
 */
export interface MembersClient {
  list(): Promise<MembersPage>;
  invite(emails: readonly string[], role: MemberRole): Promise<Invited>;
  link(invitationId: string): Promise<string>;
  sendAgain(invitationId: string): Promise<void>;
  revoke(invitationId: string): Promise<void>;
  changeRole(userId: string, role: MemberRole): Promise<void>;
  /** Removes a member, or leaves the organisation with one's own id. */
  remove(userId: string): Promise<void>;
  invitation(token: string): Promise<InvitationToJoin>;
  /** Joins; name and password create the account, none joins with the signed-in one. Returns the session state. */
  join(token: string, account?: { name: string; password: string }): Promise<string>;
}

export function createMembersClient(options: AppClientOptions): MembersClient {
  const request = createRequest(options);
  const invitationAt = (invitationId: string) => `/api/app/members/invitations/${encodeURIComponent(invitationId)}`;
  const joinAt = (token: string) => `/api/app/invitations/${encodeURIComponent(token)}`;

  return {
    list: () => request<MembersPage>("GET", "/api/app/members"),
    invite: (emails, role) => request<Invited>("POST", "/api/app/members/invitations", { emails, role }),
    link: (invitationId) => request<{ link: string }>("GET", `${invitationAt(invitationId)}/link`).then((answer) => answer.link),
    sendAgain: (invitationId) => request("POST", `${invitationAt(invitationId)}/send`),
    revoke: (invitationId) => request("DELETE", invitationAt(invitationId)),
    changeRole: (userId, role) => request("PUT", `/api/app/members/${encodeURIComponent(userId)}/role`, { role }),
    remove: (userId) => request("DELETE", `/api/app/members/${encodeURIComponent(userId)}`),
    invitation: (token) => request<InvitationToJoin>("GET", joinAt(token)),
    join: (token, account) => request<{ state: string }>("POST", `${joinAt(token)}/accept`, account ?? {}).then((answer) => answer.state),
  };
}

/** Roles a host system may give; owners are never mapped. */
export type MappedRole = Exclude<MemberRole, "owner">;

/** Organisation → Sign-in (AWK-12, W22): how a host product signs its users in and provisions them. */
export interface SignInSettings {
  readonly handoverEnabled: boolean;
  readonly issuer: string | null;
  /** Public keys in PEM, at most two. */
  readonly keys: readonly string[];
  readonly jwksUrl: string | null;
  /** The aud the host puts in its tokens; shown to copy. */
  readonly audience: string;
  readonly provisioningEnabled: boolean;
  /** The host system's name, shown as "Managed by …". */
  readonly systemName: string | null;
  readonly roleRules: readonly { group: string; role: MappedRole }[];
  /** The role of anyone no rule matches; null lets them not in. */
  readonly defaultRole: MappedRole | null;
  /** Single sign-on with the organisation's OpenID Connect provider (W22 step 2). */
  readonly oidcEnabled: boolean;
  readonly oidcAuthority: string | null;
  readonly oidcClientId: string | null;
  /** A client secret is stored; it is never shown. */
  readonly oidcClientSecretSet: boolean;
  /** The name on the button: "Sign in with …". */
  readonly oidcDisplayName: string | null;
  /** The redirect URI's path to register at the provider. */
  readonly oidcCallbackPath: string;
  /** The link that starts single sign-on, to share. */
  readonly ssoPath: string;
  /** Sign-in with the directory password over LDAPS (W22 step 3). */
  readonly ldapEnabled: boolean;
  readonly ldapHost: string | null;
  readonly ldapPort: number;
  readonly ldapBindDn: string | null;
  /** The search account's password is stored; it is never shown. */
  readonly ldapBindPasswordSet: boolean;
  readonly ldapBaseDn: string | null;
  readonly ldapLoginAttribute: string;
  readonly ldapObjectClass: string;
  readonly ldapGroupAttribute: string;
  readonly ldapCaCertificate: string | null;
  readonly ldapDisplayName: string | null;
}

/** What the directory says about a test person. */
export interface DirectoryTest {
  readonly ok: boolean;
  readonly problem: string | null;
  readonly email: string | null;
  readonly name: string | null;
  readonly groups: string[];
  readonly role: MappedRole | null;
}

/** What is saved: the settings without what Askwerk derives, and a new client secret (null keeps it, "" removes it). */
export type SignInSettingsChange = Omit<SignInSettings, "audience" | "oidcClientSecretSet" | "oidcCallbackPath" | "ssoPath" | "ldapBindPasswordSet"> & { readonly oidcClientSecret: string | null; readonly ldapBindPassword: string | null };

/** Owners only (403 otherwise); 400 with field errors (key-invalid, issuer-required, key-required, https-required). */
export interface SignInSettingsClient {
  get(): Promise<SignInSettings>;
  save(settings: SignInSettingsChange): Promise<SignInSettings>;
  /** Tries the saved directory settings with a test person; nothing is stored. */
  testDirectory(username: string, password: string): Promise<DirectoryTest>;
}

export function createSignInSettingsClient(options: AppClientOptions): SignInSettingsClient {
  const request = createRequest(options);

  return {
    get: () => request<SignInSettings>("GET", "/api/app/sign-in"),
    save: (settings) => request<SignInSettings>("PUT", "/api/app/sign-in", settings),
    testDirectory: (username, password) => request<DirectoryTest>("POST", "/api/app/sign-in/ldap-test", { username, password }),
  };
}
