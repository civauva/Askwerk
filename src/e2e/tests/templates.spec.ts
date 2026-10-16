// SPDX-License-Identifier: AGPL-3.0-only
// Lists as cards and templates in the designer (W32): each survey's front page in its look, and Askwerk's template
// copied to ours, edited like a survey and used for a new one.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an owner sees surveys and templates as cards, edits a copy of Askwerk's template and starts a survey from it", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Cards and template editing are checked on a desktop.");
  await page.goto("/");
  await page.getByLabel("Email", { exact: true }).fill(owner.email);
  await page.getByLabel("Password", { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys", exact: true })).toBeVisible();

  // Surveys as cards: the welcome page of each survey in its own look.
  await page.getByRole("button", { name: "▦ Cards" }).click();
  const cards = page.getByRole("list", { name: "Surveys" });
  await expect(cards.getByRole("listitem").first()).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("surveys-cards.png") });
  await page.getByRole("button", { name: "☰ List" }).click();

  // Templates: Askwerk's copied to ours opens in the designer as a template.
  await goTo(page, "Templates");
  await expect(page.getByRole("heading", { level: 1, name: "Templates" })).toBeVisible();
  await page.getByRole("button", { name: "▦ Cards" }).click();
  await expect(page.getByRole("list", { name: "Templates" }).getByRole("listitem").first()).toBeVisible();
  await page.screenshot({ path: info.outputPath("templates-cards.png") });
  await page.getByRole("button", { name: "Copy NPS, short to our templates" }).click();

  const bar = page.getByRole("banner", { name: "Survey" });
  await expect(bar.getByText("Template", { exact: true })).toBeVisible();
  await expect(bar.getByRole("button", { name: "Publish" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Share" })).toHaveCount(0);
  expect((await new AxeBuilder({ page }).exclude("iframe").analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("template-editing.png") });

  await bar.getByRole("button", { name: "Use for a new survey" }).click();
  await expect(page.getByRole("banner", { name: "Survey" }).getByRole("button", { name: "Publish" })).toBeVisible();
  await expect(page.getByRole("banner", { name: "Survey" }).getByText("Template", { exact: true })).toHaveCount(0);
});
