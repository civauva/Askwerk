// SPDX-License-Identifier: AGPL-3.0-only
// First run on a fresh installation (AWK-16): setup wizard, required two-factor setup, recovery codes, signed in.
// Runs once per e2e run, before the other projects (playwright.config.ts, dependencies).
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { owner, saveTwoFactorKey, totp } from "./owner";

test("a fresh installation runs setup, requires two-factor for the owner, and signs in", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Your account" })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);

  await page.getByLabel("Name", { exact: true }).fill(owner.name);
  await page.getByLabel("Email", { exact: true }).fill(owner.email);
  await page.getByLabel("Password", { exact: true }).fill(owner.password);
  await page.getByLabel("Repeat password").fill(owner.password);
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByLabel("Instance name").fill("Acme Surveys");
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByLabel("Organisation name").fill("Acme GmbH");
  await page.getByLabel("Short name").fill("acme");
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByRole("button", { name: "Skip for now" }).click();

  await expect(page.getByRole("heading", { name: "Set up two-factor" })).toBeVisible();
  await expect(page.getByRole("img", { name: "QR code for your authenticator app" })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  const key = (await page.locator(".auth__secret").textContent())!.replace(/\s/g, "");
  saveTwoFactorKey(key);
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(key));
  await page.getByRole("button", { name: "Confirm and continue" }).click();

  await expect(page.getByRole("heading", { name: "Keep your recovery codes" })).toBeVisible();
  await expect(page.getByRole("listitem")).toHaveCount(10);
  await page.getByRole("button", { name: "I have saved them" }).click();

  await expect(page.getByRole("button", { name: "Organisation: Acme GmbH" })).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys", exact: true })).toBeVisible();
});
