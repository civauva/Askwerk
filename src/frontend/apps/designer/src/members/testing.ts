// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written MembersClient for the Members page's and the join page's tests.
import { ApiError, type InvitationToJoin, type MemberRole, type MembersClient, type MembersPage } from "@askwerk/app-client";

export const sampleMembers: MembersPage = {
  members: [
    { id: "u1", name: "Dražan Kosić", email: "drazan@acme.at", role: "owner", twoFactor: true, lastSignInAt: "2026-10-15T08:00:00Z", you: true },
    { id: "u2", name: "Anna Berger", email: "anna@acme.at", role: "admin", twoFactor: true, lastSignInAt: "2026-10-15T07:00:00Z", you: false },
    { id: "u3", name: "Ben Huber", email: "ben@acme.at", role: "editor", twoFactor: false, lastSignInAt: "2026-10-14T16:00:00Z", you: false },
    { id: "u4", name: "Josef Huber", email: "josef@acme.at", role: "viewer", twoFactor: true, lastSignInAt: null, you: false },
  ],
  invitations: [
    { id: "i1", email: "eva@acme.at", role: "editor", invitedBy: "Anna Berger", invitedAt: "2026-10-15T08:00:00Z", expiresAt: "2026-10-22T08:00:00Z", sentAt: "2026-10-15T08:00:00Z", open: true },
  ],
  canManageOwners: true,
  emailReady: false,
};

export const sampleInvitation: InvitationToJoin = {
  organisation: "Acme GmbH",
  role: "editor",
  invitedBy: "Anna Berger",
  email: "ben@acme.at",
  accountExists: false,
  signedInAs: null,
  expiresAt: "2026-10-22T08:00:00Z",
};

export class FakeMembersClient implements MembersClient {
  readonly calls: [string, ...unknown[]][] = [];
  page: MembersPage = sampleMembers;
  refuse = false;
  joinable: InvitationToJoin | "gone" = sampleInvitation;
  joinState = "signed-in";

  list = () => {
    this.calls.push(["list"]);
    return this.refuse ? Promise.reject(new ApiError(403, undefined, "forbidden", {})) : Promise.resolve(this.page);
  };

  invite = (emails: readonly string[], role: MemberRole) => {
    this.calls.push(["invite", [...emails], role]);
    return Promise.resolve({
      invitations: emails.map((email, index) => ({ id: `n${index}`, email, link: `https://surveys.acme.at/join/token${index}` })),
      alreadyMembers: [],
      sent: this.page.emailReady,
    });
  };

  link = (invitationId: string) => {
    this.calls.push(["link", invitationId]);
    return Promise.resolve("https://surveys.acme.at/join/abc");
  };

  sendAgain = (invitationId: string) => {
    this.calls.push(["sendAgain", invitationId]);
    return Promise.resolve();
  };

  revoke = (invitationId: string) => {
    this.calls.push(["revoke", invitationId]);
    return Promise.resolve();
  };

  changeRole = (userId: string, role: MemberRole) => {
    this.calls.push(["changeRole", userId, role]);
    return Promise.resolve();
  };

  remove = (userId: string) => {
    this.calls.push(["remove", userId]);
    return Promise.resolve();
  };

  invitation = (token: string) => {
    this.calls.push(["invitation", token]);
    return this.joinable === "gone" ? Promise.reject(new ApiError(410, undefined, "gone", { code: "invitation-invalid" })) : Promise.resolve(this.joinable);
  };

  join = (token: string, account?: { name: string; password: string }) => {
    this.calls.push(["join", token, account]);
    return Promise.resolve(this.joinState);
  };
}
