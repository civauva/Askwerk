// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { createMembersClient } from "./members";

const telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

describe("createMembersClient (AWK-30)", () => {
  it("lists, invites, copies a link, sends again, revokes, changes a role and removes", async () => {
    const requests: Request[] = [];
    const answers = [
      Response.json({ members: [], invitations: [], canManageOwners: true, emailReady: false }),
      Response.json({ invitations: [{ id: "i1", email: "ben@acme.at", link: "https://surveys.acme.at/join/abc" }], alreadyMembers: [], sent: false }),
      Response.json({ link: "https://surveys.acme.at/join/abc" }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
      new Response(null, { status: 204 }),
    ];
    const client = createMembersClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    await client.list();
    const invited = await client.invite(["ben@acme.at"], "editor");
    const link = await client.link("i1");
    await client.sendAgain("i1");
    await client.revoke("i1");
    await client.changeRole("u2", "viewer");
    await client.remove("u2");

    expect([invited.invitations[0]?.link, link]).toEqual(["https://surveys.acme.at/join/abc", "https://surveys.acme.at/join/abc"]);
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      "GET /api/app/members",
      "POST /api/app/members/invitations",
      "GET /api/app/members/invitations/i1/link",
      "POST /api/app/members/invitations/i1/send",
      "DELETE /api/app/members/invitations/i1",
      "PUT /api/app/members/u2/role",
      "DELETE /api/app/members/u2",
    ]);
    expect(await requests[1]?.json()).toEqual({ emails: ["ben@acme.at"], role: "editor" });
    expect(await requests[5]?.json()).toEqual({ role: "viewer" });
  });

  it("shows an invitation and joins with a new account", async () => {
    const requests: Request[] = [];
    const answers = [
      Response.json({ organisation: "Acme GmbH", role: "editor", invitedBy: "Anna Berger", email: "ben@acme.at", accountExists: false, signedInAs: null, expiresAt: "2026-10-22T10:00:00Z" }),
      Response.json({ state: "signed-in" }),
    ];
    const client = createMembersClient({
      fetch: async (input, init) => {
        requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
        return answers.shift()!;
      },
      telemetry,
    });

    const invitation = await client.invitation("abc");
    const state = await client.join("abc", { name: "Ben Huber", password: "a long enough password" });

    expect([invitation.organisation, state]).toEqual(["Acme GmbH", "signed-in"]);
    expect(requests.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual(["GET /api/app/invitations/abc", "POST /api/app/invitations/abc/accept"]);
  });
});
