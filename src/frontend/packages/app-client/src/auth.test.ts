// SPDX-License-Identifier: AGPL-3.0-only
import type { Telemetry } from "@askwerk/telemetry";
import { describe, expect, it } from "vitest";
import { createAuthClient, type AuthClient } from "./auth";
import { ApiError } from "./client";

const telemetry: Telemetry = { metric: () => undefined, error: () => undefined, warn: () => undefined, flush: () => undefined };

function fakeFetch(respond: () => Response) {
  const requests: Request[] = [];
  const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), "http://askwerk.test"), init));
    return respond();
  };

  return { requests, fetch };
}

describe("createAuthClient", () => {
  it("signs in with a JSON POST to /api/app/session and returns the state", async () => {
    const { requests, fetch } = fakeFetch(() => Response.json({ state: "two-factor-required" }));
    const client = createAuthClient({ fetch, telemetry });

    const session = await client.signIn("anna@acme.at", "correct horse battery");

    expect(session).toEqual({ state: "two-factor-required" });
    expect(requests[0]?.method).toBe("POST");
    expect(new URL(requests[0]!.url).pathname).toBe("/api/app/session");
    expect(await requests[0]?.json()).toEqual({ email: "anna@acme.at", password: "correct horse battery" });
    expect(requests[0]?.headers.get("Content-Type")).toBe("application/json");
  });

  it("puts the problem's code, tries left and lock time on the ApiError", async () => {
    const problem = { code: "invalid-credentials", triesLeft: 4, lockedUntil: null, traceId: "4bf92f3577b34da6a3ce929d0e0e4736" };
    const { fetch } = fakeFetch(() => Response.json(problem, { status: 401 }));
    const client = createAuthClient({ fetch, telemetry });

    const error = await client.signIn("anna@acme.at", "wrong").catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 401, traceId: "4bf92f3577b34da6a3ce929d0e0e4736", problem: { code: "invalid-credentials", triesLeft: 4 } });
  });

  it("puts the field errors of a validation problem on the ApiError", async () => {
    const problem = { status: 400, errors: { slug: ["slug-taken"] }, traceId: "a3ce929d0e0e4736a3ce929d0e0e4736" };
    const { fetch } = fakeFetch(() => Response.json(problem, { status: 400 }));
    const client = createAuthClient({ fetch, telemetry });

    const error = await client.completeSetup({
      owner: { name: "Anna Berger", email: "anna@acme.at", password: "correct horse battery" },
      instance: { name: "Acme Surveys", baseAddress: "https://surveys.acme.at", defaultLanguage: "de", timeZone: "Europe/Vienna" },
      organisation: { name: "Acme GmbH", slug: "acme" },
    }).catch((caught: unknown) => caught);

    expect(error).toMatchObject({ status: 400, problem: { errors: { slug: ["slug-taken"] } } });
  });

  it("accepts answers without a body", async () => {
    const { requests, fetch } = fakeFetch(() => new Response(null, { status: 204 }));
    const client = createAuthClient({ fetch, telemetry });

    await client.signOut();

    expect(requests[0]?.method).toBe("DELETE");
  });

  it("asks whether setup is needed and treats 404 as done", async () => {
    const { fetch } = fakeFetch(() => Response.json({ status: 404 }, { status: 404 }));
    const client = createAuthClient({ fetch, telemetry });

    const needed = await client.setupRequired();

    expect(needed).toBe(false);
  });

  it("reads the account from /api/app/me and treats 401 as signed out", async () => {
    const { fetch } = fakeFetch(() => new Response(null, { status: 401 }));
    const client = createAuthClient({ fetch, telemetry });

    const me = await client.me();

    expect(me).toBeNull();
  });

  it("reads the instance name for the sign-in page", async () => {
    const { fetch } = fakeFetch(() => Response.json({ name: "Acme Surveys" }));
    const client = createAuthClient({ fetch, telemetry });

    const name = await client.instanceName();

    expect(name).toBe("Acme Surveys");
  });

  it.each([
    ["changePassword", (client: AuthClient) => client.changePassword("old one", "a brand new passphrase"), "POST", "/api/app/account/password", { currentPassword: "old one", newPassword: "a brand new passphrase" }],
    ["resetTwoFactor", (client: AuthClient) => client.resetTwoFactor("pw"), "POST", "/api/app/account/two-factor/reset", { password: "pw" }],
    ["disableTwoFactor", (client: AuthClient) => client.disableTwoFactor("pw"), "POST", "/api/app/account/two-factor/disable", { password: "pw" }],
    ["savePreferences", (client: AuthClient) => client.savePreferences("de", "dark"), "PUT", "/api/app/account/preferences", { language: "de", theme: "dark" }],
    ["signOutEverywhere", (client: AuthClient) => client.signOutEverywhere(), "POST", "/api/app/account/sign-out-everywhere", undefined],
    ["addPasskey", (client: AuthClient) => client.addPasskey('{"id":"abc"}', "Pixel 7"), "POST", "/api/app/account/passkeys", { credential: '{"id":"abc"}', name: "Pixel 7" }],
    ["renamePasskey", (client: AuthClient) => client.renamePasskey("a-b_c", "Pixel 8"), "PUT", "/api/app/account/passkeys/a-b_c", { name: "Pixel 8" }],
    ["removePasskey", (client: AuthClient) => client.removePasskey("a-b_c", "pw"), "POST", "/api/app/account/passkeys/a-b_c/remove", { password: "pw" }],
    ["signInWithPasskey", (client: AuthClient) => client.signInWithPasskey('{"id":"abc"}'), "POST", "/api/app/session/passkey", { credential: '{"id":"abc"}' }],
  ] as const)("%s sends the account and security request", async (_, call, method, path, body) => {
    const { requests, fetch } = fakeFetch(() => Response.json({ state: "signed-in" }));
    const client = createAuthClient({ fetch, telemetry });

    await call(client);

    expect(requests[0]?.method).toBe(method);
    expect(new URL(requests[0]!.url).pathname).toBe(path);
    expect(body === undefined ? undefined : await requests[0]?.json()).toEqual(body);
  });

  it("returns the new recovery codes", async () => {
    const { requests, fetch } = fakeFetch(() => Response.json({ recoveryCodes: ["AAAAA-11111", "BBBBB-22222"] }));
    const client = createAuthClient({ fetch, telemetry });

    const codes = await client.newRecoveryCodes("pw");

    expect(codes).toEqual(["AAAAA-11111", "BBBBB-22222"]);
    expect(new URL(requests[0]!.url).pathname).toBe("/api/app/account/two-factor/recovery-codes");
  });

  it("asks for passkey options as JSON objects for the browser", async () => {
    const { requests, fetch } = fakeFetch(() => Response.json({ challenge: "Y2hhbGxlbmdl" }));
    const client = createAuthClient({ fetch, telemetry });

    const creation = await client.passkeyCreationOptions();
    const request = await client.passkeyRequestOptions();

    expect([creation, request]).toEqual([{ challenge: "Y2hhbGxlbmdl" }, { challenge: "Y2hhbGxlbmdl" }]);
    expect(requests.map((sent) => `${sent.method} ${new URL(sent.url).pathname}`)).toEqual(["POST /api/app/account/passkeys/options", "POST /api/app/session/passkey/options"]);
  });

  it("lists the passkeys", async () => {
    const passkey = { id: "abc", name: "Pixel 7", createdAt: "2026-10-13T08:00:00Z", lastUsedAt: null };
    const { fetch } = fakeFetch(() => Response.json([passkey]));
    const client = createAuthClient({ fetch, telemetry });

    expect(await client.passkeys()).toEqual([passkey]);
  });
});
