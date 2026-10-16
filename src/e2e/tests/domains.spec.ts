// SPDX-License-Identifier: AGPL-3.0-only
// Domains (AWK-7, W12 A): an owner adds a domain and sees the two DNS records to set.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": { organisation: /^Organisation: /, menu: "Domains", add: "Add a domain", button: "Add domain", waiting: "Waiting for DNS", verify: "Verify", records: "DNS records" },
  "phone-de": { organisation: /^Organisation: /, menu: "Domains", add: "Domain hinzufügen", button: "Domain hinzufügen", waiting: "Wartet auf DNS", verify: "Prüfen", records: "DNS-Einträge" },
} as const;

test("an owner adds a domain and sees the records to set at the DNS provider", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  await goTo(page, t.menu);
  await expect(page.getByRole("heading", { level: 1, name: "Domains" })).toBeVisible();
  const hostname = `umfragen-${info.project.name}.e2e.example`;
  await page.getByRole("textbox", { name: t.add }).fill(hostname);
  await page.getByRole("button", { name: t.button, exact: true }).click();

  const domain = page.getByRole("region", { name: hostname });
  await expect(domain.getByText(t.waiting)).toBeVisible();
  await expect(domain.getByRole("table", { name: t.records })).toContainText(`_askwerk.${hostname}`);
  await domain.getByRole("button", { name: t.verify }).click();
  await expect(domain.getByText(t.waiting)).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`domains-${info.project.name}.png`), fullPage: true });
});
