// SPDX-License-Identifier: AGPL-3.0-only
// Organisation → API (AWK-25, W14 C and D): an owner creates a key, sees it once, and another system reads with it.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": { organisation: /^Organisation: /, create: "Create key", name: "Name", confirm: "Create", created: "Key created", done: "Done", keys: "API keys" },
  "phone-de": { organisation: /^Organisation: /, create: "Schlüssel erstellen", name: "Name", confirm: "Erstellen", created: "Schlüssel erstellt", done: "Fertig", keys: "API-Schlüssel" },
} as const;

test("an owner creates an API key, and another system reads the surveys with it", async ({ page, playwright }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  // Developers & API in Administration (AWK-38), then its keys.
  await goTo(page, /^(Developers & API|Entwickler & API)$/);
  await page.getByRole("link", { name: /^(Your API keys|Ihre API-Schlüssel) →$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "API" })).toBeVisible();
  await page.getByRole("button", { name: t.create }).click();
  const name = `Reporting ${info.project.name}`;
  await page.getByRole("dialog").getByRole("textbox", { name: t.name }).fill(name);
  await page.getByRole("dialog").getByRole("button", { name: t.confirm, exact: true }).click();

  const created = page.getByRole("dialog", { name: t.created });
  const secret = (await created.locator("code").textContent()) ?? "";
  expect(secret).toMatch(/^awk_[A-Za-z0-9]{44}$/);
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`api-key-created-${info.project.name}.png`), fullPage: true });
  await created.getByRole("button", { name: t.done }).click();
  await expect(page.getByRole("grid", { name: t.keys })).toContainText(name);
  await expect(page.getByText(secret)).toHaveCount(0);
  await page.screenshot({ path: info.outputPath(`api-keys-${info.project.name}.png`), fullPage: true });

  const api = await playwright.request.newContext({ baseURL: info.project.use.baseURL, extraHTTPHeaders: { Authorization: `Bearer ${secret}` } });
  const surveys = await api.get("/api/v1/surveys");
  expect(surveys.status()).toBe(200);
  expect(Array.isArray(((await surveys.json()) as { items: unknown[] }).items)).toBe(true);
  await api.dispose();
});
