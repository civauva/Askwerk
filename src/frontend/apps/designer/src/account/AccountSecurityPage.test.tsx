// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AccountSecurityPage } from "./AccountSecurityPage";
import { anna, FakeAuthClient, FakePasskeyDevice, problem } from "./testing";

afterEach(cleanup);

const now = () => new Date("2026-10-14T12:00:00Z");

function renderPage({ client = new FakeAuthClient(), device = new FakePasskeyDevice(), me = anna, locale = "en" as "en" | "de" } = {}) {
  const onLocaleChange = vi.fn();
  const onThemeChange = vi.fn();
  const onSessionChanged = vi.fn();
  const view = render(
    <AccountSecurityPage
      client={client}
      device={device}
      me={me}
      locale={locale}
      onLocaleChange={onLocaleChange}
      onThemeChange={onThemeChange}
      onSessionChanged={onSessionChanged}
      now={now}
      timeZone="UTC"
    />,
  );

  return { client, device, onLocaleChange, onThemeChange, onSessionChanged, ...view };
}

const section = (name: string) => screen.getByRole("region", { name });

/** Picks an option of a shared combo box (AWK-19): open it by its label, then click the option. */
function choose(scope: { getByRole: typeof screen.getByRole }, label: string, option: string) {
  const button = scope.getByRole("button", { name: new RegExp(`^${label}( |$)`) });
  expect(button.getAttribute("aria-haspopup")).toBe("listbox");
  fireEvent.click(button);
  fireEvent.click(screen.getByRole("option", { name: option }));
}


async function confirmWithPassword(password = "correct horse battery") {
  const dialog = await screen.findByRole("dialog", { name: "Confirm it is you" });
  fireEvent.change(within(dialog).getByLabelText("Password"), { target: { value: password } });
  fireEvent.submit(within(dialog).getByLabelText("Password").closest("form")!);
  return dialog;
}

describe("AccountSecurityPage (W4)", () => {
  it("names the account, the organisation and the role", async () => {
    renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Account and security" })).toBeTruthy();
    expect(screen.getByText("Anna Berger · anna@acme.at · Owner of Acme GmbH")).toBeTruthy();
    await screen.findByText("Pixel 7");
  });

  it("is in German for German users", async () => {
    renderPage({ locale: "de" });

    expect(screen.getByRole("heading", { level: 1, name: "Konto und Sicherheit" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Überall abmelden" })).toBeTruthy();
    await screen.findByText("Pixel 7");
  });

  it("lists the passkeys with when they were added and last used", async () => {
    renderPage();

    const passkeys = section("Passkeys");
    expect(await within(passkeys).findByText("MacBook · Touch ID")).toBeTruthy();
    expect(within(passkeys).getByText("Added 13 Oct 2026 · last used today")).toBeTruthy();
    expect(within(passkeys).getByText("Added 13 Oct 2026 · never used")).toBeTruthy();
  });

  it("adds a passkey: the browser asks first, then an optional name", async () => {
    const { client, device } = renderPage();
    await screen.findByText("Pixel 7");

    fireEvent.click(screen.getByRole("button", { name: "Add a passkey" }));
    const dialog = await screen.findByRole("dialog", { name: "Add a passkey" });
    fireEvent.change(within(dialog).getByLabelText("Name (so you recognise it)"), { target: { value: "Work laptop" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(client.calls).toContainEqual(["addPasskey", '{"id":"bmV3"}', "Work laptop"]));
    expect(device.calls).toEqual([["create", { challenge: "Y3JlYXRl" }]]);
    expect(client.calls.filter(([name]) => name === "passkeys")).toHaveLength(2);
  });

  it("says so when the passkey was not added, and adds nothing", async () => {
    const device = new FakePasskeyDevice();
    device.answer = new DOMException("The operation was cancelled.", "NotAllowedError");
    const { client } = renderPage({ device });
    await screen.findByText("Pixel 7");

    fireEvent.click(screen.getByRole("button", { name: "Add a passkey" }));

    expect(await screen.findByText("The passkey was not added.")).toBeTruthy();
    expect(client.calls.map(([name]) => name)).not.toContain("addPasskey");
  });

  it("explains when the browser cannot use passkeys", async () => {
    const device = new FakePasskeyDevice();
    device.supported = false;
    renderPage({ device });

    expect(await screen.findByText("This browser does not support passkeys.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Add a passkey" }).hasAttribute("disabled")).toBe(true);
  });

  it("renames a passkey", async () => {
    const { client } = renderPage();

    fireEvent.click(await screen.findByRole("button", { name: "Rename Pixel 7" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Name of Pixel 7" }), { target: { value: "Pixel 8" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(client.calls).toContainEqual(["renamePasskey", "cGl4ZWw", "Pixel 8"]));
  });

  it("removes a passkey only after the password", async () => {
    const client = new FakeAuthClient();
    client.confirmAnswers = [problem(400, { errors: { password: ["password-wrong"] } }), undefined];
    renderPage({ client });

    fireEvent.click(await screen.findByRole("button", { name: "Remove Pixel 7" }));
    const dialog = await confirmWithPassword("wrong");
    expect(await within(dialog).findByText("The password is wrong.")).toBeTruthy();
    await confirmWithPassword();

    await waitFor(() => expect(client.calls).toContainEqual(["removePasskey", "cGl4ZWw", "correct horse battery"]));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("shows two-factor as on, with the recovery codes left", () => {
    renderPage();

    const twoFactor = section("Two-factor (authenticator app)");
    expect(within(twoFactor).getByText("On")).toBeTruthy();
    expect(within(twoFactor).getByText("8 of 10 recovery codes left")).toBeTruthy();
  });

  it("shows “Turn off” disabled for owners and admins, with the reason", () => {
    renderPage();

    expect(screen.getByRole("button", { name: "Turn off" }).hasAttribute("disabled")).toBe(true);
    expect(screen.getByText("“Turn off” is disabled for your role: required for owners and admins.")).toBeTruthy();
  });

  it("turns two-factor off for members after the password", async () => {
    const { client } = renderPage({ me: { ...anna, twoFactorRequired: false, organisations: [{ ...anna.organisations[0]!, role: "editor" }] } });

    fireEvent.click(screen.getByRole("button", { name: "Turn off" }));
    await confirmWithPassword();

    await waitFor(() => expect(client.calls).toContainEqual(["disableTwoFactor", "correct horse battery"]));
  });

  it("shows new recovery codes once, after the password", async () => {
    const { client } = renderPage();

    fireEvent.click(screen.getByRole("button", { name: "New recovery codes" }));
    await confirmWithPassword();

    expect(await screen.findByText("CCCCC-33333")).toBeTruthy();
    expect(client.calls).toContainEqual(["newRecoveryCodes", "correct horse battery"]);
  });

  it("moves to a new phone: after the password the setup starts again", async () => {
    const { client, onSessionChanged } = renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Move to a new phone" }));
    await confirmWithPassword();

    await waitFor(() => expect(onSessionChanged).toHaveBeenCalledWith("two-factor-setup-required"));
    expect(client.calls).toContainEqual(["resetTwoFactor", "correct horse battery"]);
  });

  it("changes the password and says the other sessions are signed out", async () => {
    const { client } = renderPage();
    const password = section("Password");

    fireEvent.change(within(password).getByLabelText("Current password"), { target: { value: "correct horse battery" } });
    fireEvent.change(within(password).getByLabelText("New password"), { target: { value: "a brand new passphrase" } });
    fireEvent.change(within(password).getByLabelText("Repeat new password"), { target: { value: "a brand new passphrase" } });
    fireEvent.click(within(password).getByRole("button", { name: "Change password" }));

    expect(await within(password).findByText("Password changed. You are signed out everywhere else.")).toBeTruthy();
    expect(client.calls).toContainEqual(["changePassword", "correct horse battery", "a brand new passphrase"]);
  });

  it("refuses different new passwords without asking the server", () => {
    const { client } = renderPage();
    const password = section("Password");

    fireEvent.change(within(password).getByLabelText("Current password"), { target: { value: "correct horse battery" } });
    fireEvent.change(within(password).getByLabelText("New password"), { target: { value: "a brand new passphrase" } });
    fireEvent.change(within(password).getByLabelText("Repeat new password"), { target: { value: "a brand new passphrose" } });
    fireEvent.click(within(password).getByRole("button", { name: "Change password" }));

    expect(within(password).getByText("The new passwords differ.")).toBeTruthy();
    expect(client.calls.map(([name]) => name)).not.toContain("changePassword");
  });

  it("shows the server's answer at the field", async () => {
    const client = new FakeAuthClient();
    client.passwordAnswers = [problem(400, { errors: { currentPassword: ["password-wrong"] } })];
    renderPage({ client });
    const password = section("Password");

    fireEvent.change(within(password).getByLabelText("Current password"), { target: { value: "wrong" } });
    fireEvent.change(within(password).getByLabelText("New password"), { target: { value: "a brand new passphrase" } });
    fireEvent.change(within(password).getByLabelText("Repeat new password"), { target: { value: "a brand new passphrase" } });
    fireEvent.click(within(password).getByRole("button", { name: "Change password" }));

    expect(await within(password).findByText("The password is wrong.")).toBeTruthy();
    expect(within(password).getByLabelText("Current password").getAttribute("aria-invalid")).toBe("true");
  });

  it("saves language and theme as they change", async () => {
    const { client, onLocaleChange, onThemeChange } = renderPage();

    choose(screen, "Language", "Deutsch");
    await waitFor(() => expect(client.calls).toContainEqual(["savePreferences", "de", "system"]));
    choose(screen, "Theme", "Dark");

    await waitFor(() => expect(client.calls).toContainEqual(["savePreferences", "de", "dark"]));
    expect(onLocaleChange).toHaveBeenCalledWith("de");
    expect(onThemeChange).toHaveBeenCalledWith("dark");
  });

  it("signs out everywhere else", async () => {
    const { client } = renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Sign out everywhere" }));

    expect(await screen.findByText("Signed out on all other browsers and devices.")).toBeTruthy();
    expect(client.calls).toContainEqual(["signOutEverywhere"]);
  });

  it("has no accessibility violations", async () => {
    const { container } = renderPage();
    await screen.findByText("Pixel 7");

    await expectNoAxeViolations(container);
  });
});
