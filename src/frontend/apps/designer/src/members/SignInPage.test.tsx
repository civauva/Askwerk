// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type SignInSettings, type SignInSettingsChange, type SignInSettingsClient } from "@askwerk/app-client";
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { SignInPage } from "./SignInPage";

afterEach(cleanup);

const off: SignInSettings = { handoverEnabled: false, issuer: null, keys: [], jwksUrl: null, audience: "askwerk:t-1", provisioningEnabled: false, systemName: null, roleRules: [], defaultRole: "viewer", oidcEnabled: false, oidcAuthority: null, oidcClientId: null, oidcClientSecretSet: false, oidcDisplayName: null, oidcCallbackPath: "/sso/callback/t1", ssoPath: "/sso/acme", ldapEnabled: false, ldapHost: null, ldapPort: 636, ldapBindDn: null, ldapBindPasswordSet: false, ldapBaseDn: null, ldapLoginAttribute: "uid", ldapObjectClass: "person", ldapGroupAttribute: "memberOf", ldapCaCertificate: null, ldapDisplayName: null };

class FakeClient implements SignInSettingsClient {
  saved: SignInSettingsChange[] = [];
  refuse = false;
  invalid: Record<string, string[]> | null = null;

  testDirectory = (username: string) =>
    Promise.resolve({ ok: username === "clara", problem: username === "clara" ? null : "invalid-credentials", email: "clara@acme.at", name: "Clara Weiss", groups: ["HR-Admins"], role: "admin" as const });

  get = () => (this.refuse ? Promise.reject(new ApiError(403, undefined, "forbidden", {})) : Promise.resolve(off));

  save = (settings: SignInSettingsChange) => {
    this.saved.push(settings);
    return this.invalid === null ? Promise.resolve({ ...off, ...settings, oidcClientSecretSet: settings.oidcClientSecret !== null, ldapBindPasswordSet: settings.ldapBindPassword !== null }) : Promise.reject(new ApiError(400, undefined, "invalid", { errors: this.invalid }));
  };
}

async function shown(client = new FakeClient(), locale: "de" | "en" = "en") {
  const view = render(<SignInPage client={client} locale={locale} writeClipboard={() => Promise.resolve()} origin="https://surveys.acme.at" />);
  await screen.findByRole("heading", { name: locale === "de" ? "Anmeldung" : "Sign-in" });
  await act(async () => {});

  return { client, ...view };
}

describe("Organisation → Sign-in (AWK-12, W22)", () => {
  it("is off by default: Askwerk accounts and two switches, nothing else", async () => {
    const { container } = await shown();

    expect(screen.getByRole("switch", { name: "Askwerk accounts" }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByRole("switch", { name: "Hand-over from your product" }).getAttribute("aria-checked")).toBe("false");
    expect(screen.getByText("Connect your own sign-in")).toBeTruthy();
    expect(screen.queryByLabelText("Issuer (iss)")).toBeNull();
    await expectNoAxeViolations(container);
  });

  it("sets up the hand-over with a key, the audience to copy and a role rule", async () => {
    const { client, container } = await shown();

    fireEvent.click(screen.getByRole("switch", { name: "Hand-over from your product" }));
    fireEvent.change(screen.getByLabelText("Issuer (iss)"), { target: { value: "https://hr.example.org" } });
    fireEvent.change(screen.getByLabelText("Public key (PEM)"), { target: { value: "-----BEGIN PUBLIC KEY-----" } });
    expect(screen.getByText("askwerk:t-1")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Add rule" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Group of rule 1" }), { target: { value: "HR-Admins" } });
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText("Saved.");

    expect(client.saved[0]).toMatchObject({ handoverEnabled: true, issuer: "https://hr.example.org", keys: ["-----BEGIN PUBLIC KEY-----"], roleRules: [{ group: "HR-Admins", role: "viewer" }], defaultRole: "viewer" });
  });

  it("sets up single sign-on and shows the redirect URI and the link to share", async () => {
    const { client, container } = await shown();

    fireEvent.click(screen.getByRole("switch", { name: "Single sign-on (OpenID Connect)" }));
    fireEvent.change(screen.getByLabelText("Issuer URL"), { target: { value: "https://login.example.org" } });
    fireEvent.change(screen.getByLabelText("Client id"), { target: { value: "askwerk" } });
    fireEvent.change(screen.getByLabelText("Client secret"), { target: { value: "s3cret" } });
    expect(screen.getByText("https://surveys.acme.at/sso/callback/t1")).toBeTruthy();
    expect(screen.getByText("https://surveys.acme.at/sso/acme")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Roles from your system" })).toBeTruthy();
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText("Saved.");

    expect(client.saved[0]).toMatchObject({ oidcEnabled: true, oidcAuthority: "https://login.example.org", oidcClientId: "askwerk", oidcClientSecret: "s3cret" });
    expect(screen.getByText("A secret is stored. Type a new one to replace it.")).toBeTruthy();
  });

  it("sets up the directory for Active Directory, saves it and tests a person", async () => {
    const { client, container } = await shown();

    fireEvent.click(screen.getByRole("switch", { name: "Directory (LDAP, Active Directory)" }));
    fireEvent.click(screen.getByRole("radio", { name: "Active Directory" }));
    fireEvent.change(screen.getByLabelText("Server"), { target: { value: "dc1.acme.local" } });
    fireEvent.change(screen.getByLabelText("Search account (DN)"), { target: { value: "cn=askwerk,dc=acme,dc=local" } });
    fireEvent.change(screen.getByLabelText("Search account password"), { target: { value: "secret" } });
    fireEvent.change(screen.getByLabelText("Where people are (base DN)"), { target: { value: "dc=acme,dc=local" } });
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText("Saved.");

    expect(client.saved[0]).toMatchObject({ ldapEnabled: true, ldapHost: "dc1.acme.local", ldapLoginAttribute: "sAMAccountName", ldapObjectClass: "user", ldapBindPassword: "secret" });
    fireEvent.change(screen.getByRole("textbox", { name: "User name" }), { target: { value: "clara" } });
    fireEvent.click(screen.getByRole("button", { name: "Test" }));
    expect((await screen.findByText(/^Works:/)).textContent).toBe("Works: Clara Weiss, clara@acme.at — would sign in as Admin.");
  });

  it("shows the server's field errors in German", async () => {
    const client = new FakeClient();
    client.invalid = { Keys: ["key-invalid"] };
    await shown(client, "de");

    fireEvent.click(screen.getByRole("switch", { name: "Übergabe aus Ihrem Produkt" }));
    fireEvent.click(screen.getByRole("button", { name: "Speichern" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Das ist kein öffentlicher RSA- oder EC-Schlüssel im PEM-Format.");
  });

  it("tells admins that only owners change it", async () => {
    const client = new FakeClient();
    client.refuse = true;
    render(<SignInPage client={client} locale="en" />);

    expect((await screen.findByRole("alert")).textContent).toBe("Only owners change how people sign in.");
  });
});
