// SPDX-License-Identifier: AGPL-3.0-only
// W44 (AWK-69, AWK-70): a click on a part of the Look preview opens its settings, in the survey and in Administration;
// the language switch is a menu with each language's own name.
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

test("a click on a part of the preview opens its settings, and the language menu switches the survey", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Pointer flows, on a desktop.");
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();

  const created = await page.request.post("/api/app/samples/lodge", { data: { title: "Pick lodge" } });
  const { id } = (await created.json()) as { id: string };

  // The survey's Look: the welcome page opens Pages; a choice opens Parts → Components → Choices.
  await page.goto(`/surveys/${id}/look`);
  const settings = page.getByRole("complementary", { name: "Look settings" });
  const frame = page.frameLocator("iframe[title='Survey preview']");
  await expect(frame.locator(".run-welcome")).toBeVisible();
  await frame.locator(".run-welcome h1, .run-welcome h2").first().hover();
  await expect(frame.locator(".run-look-label")).toBeVisible();
  await page.screenshot({ path: info.outputPath("look-pick-hover.png") });
  await frame.locator(".run-welcome").click();
  await expect(settings.getByRole("tab", { name: "Pages" })).toHaveAttribute("aria-selected", "true");

  await settings.getByRole("tab", { name: "Parts" }).click();
  await settings.getByRole("button", { name: /^Components/ }).click();
  await settings.getByRole("button", { name: /^Choices/ }).click();
  await settings.getByRole("button", { name: /^Choices/ }).click();
  await expect(frame.locator(".run-choice").first()).toBeVisible();
  await settings.getByRole("tab", { name: "Style" }).click();
  await frame.locator(".run-choice").first().click();
  await expect(settings.getByRole("button", { name: /^Choices/ })).toHaveAttribute("aria-expanded", "true");
  await page.screenshot({ path: info.outputPath("look-pick-choices.png") });

  // Administration → Looks: the card opens its colour under More.
  await page.goto("/look");
  const preview = page.getByRole("region", { name: "Preview" });
  await preview.locator("[data-pick='surface']").click({ position: { x: 12, y: 12 } });
  await expect(page.locator("[data-field='surface']")).toBeVisible();
  await page.screenshot({ path: info.outputPath("looks-pick.png") });

  // The survey itself: the language menu lists each language by its own name.
  await page.goto(`/s/${id}?preview`);
  await page.getByRole("button", { name: /: English$/ }).click();
  await page.screenshot({ path: info.outputPath("language-menu.png") });
  await page.getByRole("menuitemradio", { name: /Deutsch/ }).click();
  await expect(page.locator("html")).toHaveAttribute("lang", "de");
  await expect(page.getByRole("button", { name: /: Deutsch$/ })).toBeVisible();
});
