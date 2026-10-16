// SPDX-License-Identifier: AGPL-3.0-only
// Signing in after the first run (AWK-16), on desktop in English and on a phone in German.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": { signIn: "Sign in", email: "Email", password: "Password", code: "Digit 1 of 6", confirm: "Confirm", running: "Surveys", failed: "Something went wrong" },
  "phone-de": { signIn: "Anmelden", email: "E-Mail", password: "Passwort", code: "Ziffer 1 von 6", confirm: "Bestätigen", running: "Umfragen", failed: "Etwas ist schiefgelaufen" },
} as const;

test("the sign-in page speaks the browser's language and has no accessibility violations, contrast included", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];

  await page.goto("/");

  await expect(page.getByRole("heading", { name: t.signIn })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
});

test("the owner signs in with password and authenticator code and reaches the app", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");

  await page.getByLabel(t.email, { exact: true }).fill(owner.email);
  await page.getByLabel(t.password, { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: t.signIn, exact: true }).click();
  await page.getByRole("textbox", { name: t.code }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: t.confirm }).click();

  await expect(page.getByRole("heading", { level: 1, name: t.running, exact: true })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
});

test("a failed API call shows its reference and reaches the server as telemetry with the same trace id", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.route("**/api/app/setup", (route) => route.abort());
  const telemetry = page.waitForRequest((request) => request.url().endsWith("/api/app/telemetry") && request.method() === "POST");

  await page.goto("/");
  await expect(page.getByRole("heading", { name: t.failed })).toBeVisible();
  const reference = (await page.getByText(/^(Reference|Referenz): /).textContent())!.split(": ")[1];
  // The page sends queued telemetry when it is hidden; trigger that without leaving the page.
  await page.evaluate(() => window.dispatchEvent(new Event("pagehide")));

  const body = (await telemetry).postDataJSON() as { records: { name: string; traceId?: string }[] };
  expect(reference).toMatch(/^[0-9a-f]{32}$/);
  expect(body.records).toContainEqual(expect.objectContaining({ name: "api.failed", traceId: reference }));
});
