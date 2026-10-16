// SPDX-License-Identifier: AGPL-3.0-only
import { expectNoAxeViolations } from "@askwerk/ui/testing";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SignInPage } from "./SignInPage";
import { FakeAuthClient, FakePasskeyDevice, problem } from "./testing";

afterEach(cleanup);

const now = () => new Date("2026-10-13T21:38:00Z");

function renderSignIn(client = new FakeAuthClient(), locale: "de" | "en" = "en", device = new FakePasskeyDevice()) {
  const onDone = vi.fn();
  const onForgotPassword = vi.fn();
  const view = render(
    <SignInPage client={client} locale={locale} instanceName="Acme Surveys" onDone={onDone} onForgotPassword={onForgotPassword} now={now} timeZone="UTC" device={device} />,
  );

  return { client, device, onDone, onForgotPassword, ...view };
}

function submitPassword(password = "correct horse battery") {
  fireEvent.change(screen.getByRole("textbox", { name: "Email" }), { target: { value: "anna@acme.at" } });
  fireEvent.change(screen.getByLabelText("Password"), { target: { value: password } });
  fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("SignInPage (M1)", () => {
  it.each([
    ["en", "Sign in", "Email", "Forgot password?"],
    ["de", "Anmelden", "E-Mail", "Passwort vergessen?"],
  ] as const)("asks for email and password in %s", (locale, heading, email, forgot) => {
    renderSignIn(new FakeAuthClient(), locale);

    expect(screen.getByRole("heading", { name: heading })).toBeTruthy();
    expect(screen.getByRole("textbox", { name: email })).toBeTruthy();
    expect(screen.getByRole("button", { name: forgot })).toBeTruthy();
  });

  it("signs in with email and password", async () => {
    const { client, onDone } = renderSignIn();

    submitPassword();

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("signed-in"));
    expect(client.calls).toContainEqual(["signIn", "anna@acme.at", "correct horse battery"]);
  });

  it("says when email or password is wrong and how many tries are left", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [problem(401, { code: "invalid-credentials", triesLeft: 4 })];
    renderSignIn(client);

    submitPassword("wrong");

    expect((await screen.findByRole("alert")).textContent).toBe("Email or password is wrong.4 tries left before a 15-minute pause.");
    expect(screen.getByLabelText("Password").getAttribute("aria-invalid")).toBe("true");
  });

  it("shows the pause with the time it ends and the minutes left", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [problem(403, { code: "locked", lockedUntil: "2026-10-13T21:47:00Z" })];
    renderSignIn(client);

    submitPassword();

    expect(await screen.findByRole("heading", { name: "Sign-in paused" })).toBeTruthy();
    expect(screen.getByText("21:47")).toBeTruthy();
    expect(screen.getByText("9 minutes left")).toBeTruthy();
    expect(screen.getByText(/Reference 4bf92f3577b34da6a3ce929d0e0e4736/)).toBeTruthy();
  });

  it("asks for the authenticator code when two-factor is on, and can trust the browser", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [{ state: "two-factor-required" }];
    const { onDone } = renderSignIn(client);
    submitPassword();
    await screen.findByRole("heading", { name: "Enter your code" });

    expect(within(screen.getByRole("group", { name: "Code from your app" })).getAllByRole("textbox")).toHaveLength(6);
    fireEvent.change(screen.getByRole("textbox", { name: "Digit 1 of 6" }), { target: { value: "482913" } });
    fireEvent.click(screen.getByRole("checkbox", { name: "Trust this browser for 30 days" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("signed-in"));
    expect(client.calls).toContainEqual(["submitTwoFactorCode", "482913", true]);
  });

  it("says when the code is wrong", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [{ state: "two-factor-required" }];
    client.codeAnswers = [problem(400, { errors: { code: ["code-invalid"] } })];
    renderSignIn(client);
    submitPassword();
    await screen.findByRole("heading", { name: "Enter your code" });

    fireEvent.change(screen.getByRole("textbox", { name: "Digit 1 of 6" }), { target: { value: "000000" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText("This code is not valid. Codes change every 30 seconds.")).toBeTruthy();
  });

  it("signs in with a recovery code instead", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [{ state: "two-factor-required" }];
    const { onDone } = renderSignIn(client);
    submitPassword();
    await screen.findByRole("heading", { name: "Enter your code" });

    fireEvent.click(screen.getByRole("button", { name: "Use a recovery code" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Recovery code" }), { target: { value: "AAAAA-11111" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("signed-in"));
    expect(client.calls).toContainEqual(["submitRecoveryCode", "AAAAA-11111"]);
  });

  it("hands over when two-factor still has to be set up", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [{ state: "two-factor-setup-required" }];
    const { onDone } = renderSignIn(client);

    submitPassword();

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("two-factor-setup-required"));
  });

  it("goes to password reset", () => {
    const { onForgotPassword } = renderSignIn();

    fireEvent.click(screen.getByRole("button", { name: "Forgot password?" }));

    expect(onForgotPassword).toHaveBeenCalled();
  });

  it.each([["en"], ["de"]] as const)("has no accessibility violations in %s", async (locale) => {
    const { container } = renderSignIn(new FakeAuthClient(), locale);
    await act(() => Promise.resolve());

    await expectNoAxeViolations(container);
  });

  it("signs in with a passkey instead of email and password (W4 E)", async () => {
    const { client, device, onDone } = renderSignIn();

    fireEvent.click(screen.getByRole("button", { name: "Sign in with a passkey" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("signed-in"));
    expect(device.calls).toEqual([["get", { challenge: "Z2V0" }]]);
    expect(client.calls).toContainEqual(["signInWithPasskey", '{"id":"bmV3"}']);
  });

  it("hides the passkey button where the browser has no passkeys", () => {
    const device = new FakePasskeyDevice();
    device.supported = false;
    renderSignIn(new FakeAuthClient(), "en", device);

    expect(screen.queryByRole("button", { name: "Sign in with a passkey" })).toBeNull();
  });

  it("says so when the passkey did not work", async () => {
    const client = new FakeAuthClient();
    client.passkeySignInAnswers = [problem(401, { code: "passkey-invalid" })];
    renderSignIn(client);

    fireEvent.click(screen.getByRole("button", { name: "Sign in with a passkey" }));

    expect((await screen.findByRole("alert")).textContent).toContain("The passkey did not work.");
  });

  it("asks owners and admins without an app code for their passkey after the password", async () => {
    const client = new FakeAuthClient();
    client.signInAnswers = [{ state: "passkey-required" }];
    const { onDone } = renderSignIn(client);

    submitPassword();
    fireEvent.click(await screen.findByRole("button", { name: "Use your passkey" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("signed-in"));
  });

  it("opens single sign-on for an organisation's short name (AWK-12)", async () => {
    const openSingleSignOn = vi.fn();
    render(<SignInPage client={new FakeAuthClient()} locale="en" instanceName="Acme Surveys" onDone={vi.fn()} onForgotPassword={vi.fn()} now={now} timeZone="UTC" device={new FakePasskeyDevice()} openSingleSignOn={openSingleSignOn} />);

    fireEvent.click(screen.getByRole("button", { name: "Sign in with single sign-on" }));
    fireEvent.change(screen.getByRole("textbox", { name: /short name/ }), { target: { value: " Acme " } });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    await waitFor(() => expect(openSingleSignOn).toHaveBeenCalledWith("acme"));
  });

  it("asks for the directory's user name and password when the organisation uses one (AWK-12)", async () => {
    const client = new FakeAuthClient();
    client.ssoAnswers = [{ oidc: false, ldap: true, oidcName: null, ldapName: "Acme Directory" }];
    const onDone = vi.fn();
    const openSingleSignOn = vi.fn();
    const { container } = render(<SignInPage client={client} locale="en" instanceName="Acme Surveys" onDone={onDone} onForgotPassword={vi.fn()} now={now} timeZone="UTC" device={new FakePasskeyDevice()} openSingleSignOn={openSingleSignOn} initialSso="acme" />);

    expect(await screen.findByRole("heading", { name: "Sign in with Acme Directory" })).toBeTruthy();
    fireEvent.change(screen.getByRole("textbox", { name: "User name" }), { target: { value: "clara" } });
    fireEvent.change(screen.getAllByLabelText("Password")[1]!, { target: { value: "secret" } });
    await expectNoAxeViolations(container);
    fireEvent.click(screen.getAllByRole("button", { name: "Sign in" })[1]!);

    await waitFor(() => expect(onDone).toHaveBeenCalledWith("signed-in"));
    expect(client.calls).toContainEqual(["signInWithDirectory", "acme", "clara", "secret"]);
    expect(openSingleSignOn).not.toHaveBeenCalled();
  });

  it("says when the directory password is wrong", async () => {
    const client = new FakeAuthClient();
    client.ssoAnswers = [{ oidc: false, ldap: true, oidcName: null, ldapName: "Acme Directory" }];
    client.directoryAnswers = [problem(401, { code: "invalid-credentials" })];
    render(<SignInPage client={client} locale="de" instanceName="Acme Surveys" onDone={vi.fn()} onForgotPassword={vi.fn()} now={now} timeZone="UTC" device={new FakePasskeyDevice()} initialSso="acme" />);

    fireEvent.change(await screen.findByRole("textbox", { name: "Benutzername" }), { target: { value: "clara" } });
    fireEvent.change(screen.getAllByLabelText("Passwort")[1]!, { target: { value: "nope" } });
    fireEvent.click(screen.getAllByRole("button", { name: "Anmelden" })[1]!);

    expect((await screen.findByRole("alert")).textContent).toBe("Benutzername oder Passwort ist falsch.");
  });
});
