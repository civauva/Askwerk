// SPDX-License-Identifier: AGPL-3.0-only
// Account and security (AWK-16, W4): a passkey added with Chromium's virtual authenticator, then used to sign in.
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": {
    signIn: "Sign in",
    email: "Email",
    password: "Password",
    code: "Digit 1 of 6",
    confirm: "Confirm",
    running: "Surveys",
    accountMenu: `Account: ${owner.name}`,
    account: "Account and security",
    signOut: "Sign out",
    addPasskey: "Add a passkey",
    passkeyName: "Name (so you recognise it)",
    save: "Save",
    passkeySignIn: "Sign in with a passkey",
  },
  "phone-de": {
    signIn: "Anmelden",
    email: "E-Mail",
    password: "Passwort",
    code: "Ziffer 1 von 6",
    confirm: "Bestätigen",
    running: "Umfragen",
    accountMenu: `Konto: ${owner.name}`,
    account: "Konto und Sicherheit",
    signOut: "Abmelden",
    addPasskey: "Passkey hinzufügen",
    passkeyName: "Name (damit Sie ihn wiedererkennen)",
    save: "Speichern",
    passkeySignIn: "Mit Passkey anmelden",
  },
} as const;

type Text = (typeof text)[keyof typeof text];

/** A platform authenticator that verifies the user (fingerprint, face or PIN) without asking. */
async function addVirtualAuthenticator(page: Page) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("WebAuthn.enable");
  await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: { protocol: "ctap2", transport: "internal", hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  });
}

async function signInWithPasswordAndCode(page: Page, t: Text) {
  await page.goto("/");
  await page.getByLabel(t.email, { exact: true }).fill(owner.email);
  await page.getByLabel(t.password, { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: t.signIn, exact: true }).click();
  await page.getByRole("textbox", { name: t.code }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: t.confirm }).click();
  await expect(page.getByRole("heading", { level: 1, name: t.running, exact: true })).toBeVisible();
}

test("the owner adds a passkey on Account and security, then signs in with it alone", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];
  const name = `Virtual ${info.project.name}`;
  await addVirtualAuthenticator(page);
  await signInWithPasswordAndCode(page, t);

  await openAccountMenu(page, t.accountMenu);
  await page.getByRole("menuitem", { name: t.account }).click();
  await expect(page.getByRole("heading", { level: 1, name: t.account })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`account-security-${info.project.name}.png`), fullPage: true });

  await page.getByRole("button", { name: t.addPasskey }).click();
  await page.getByLabel(t.passkeyName).fill(name);
  await page.getByRole("button", { name: t.save }).click();
  await expect(page.getByText(name)).toBeVisible();
  await page.screenshot({ path: info.outputPath(`account-security-passkey-${info.project.name}.png`), fullPage: true });

  await openAccountMenu(page, t.accountMenu);
  await page.getByRole("menuitem", { name: t.signOut }).click();
  await page.getByRole("button", { name: t.passkeySignIn }).click();

  await expect(page.getByRole("heading", { level: 1, name: t.running, exact: true })).toBeVisible();
});
