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
  await frame.locator(".run-welcome").click({ position: { x: 8, y: 8 } });
  await expect(settings.getByRole("tab", { name: "Pages" })).toHaveAttribute("aria-selected", "true");

  // Try walks through the survey; back in Edit, Next opens the buttons' settings (W46 A).
  const stage = page.getByRole("complementary", { name: "Preview" });
  await stage.getByRole("button", { name: "Try" }).click();
  await frame.getByRole("button", { name: "Start the survey" }).click();
  await expect(frame.locator(".run-q").first()).toBeVisible();
  await expect(frame.getByRole("button", { name: "Back" })).toBeVisible();
  await stage.getByRole("button", { name: "Edit" }).click();
  await frame.getByRole("button", { name: "Next" }).hover();
  await expect(frame.locator(".run-look-label")).toHaveText(/click to edit/);
  await page.screenshot({ path: info.outputPath("look-pick-buttons.png") });
  await frame.getByRole("button", { name: "Next" }).click();
  await expect(settings.getByRole("button", { name: /^Buttons/ })).toHaveAttribute("aria-expanded", "true");
  await expect(frame.locator(".run-q").first()).toBeVisible();

  // Components is open now; opening Choices takes the preview to a page with choices.
  await settings.getByRole("button", { name: /^Choices/ }).click();
  await expect(frame.locator(".run-choice").first()).toBeVisible();
  await settings.getByRole("tab", { name: "Style" }).click();
  await frame.locator(".run-choice").first().click();
  await expect(settings.getByRole("button", { name: /^Choices/ })).toHaveAttribute("aria-expanded", "true");
  await page.screenshot({ path: info.outputPath("look-pick-choices.png") });

  // The option groups as even tracks or menus, and the picture panel (W46 B2, B).
  await page.screenshot({ path: info.outputPath("look-options.png") });
  await settings.getByRole("tab", { name: "Style" }).click();
  await settings.getByRole("button", { name: "Picture", exact: true }).click();
  const pictures = settings.getByRole("region", { name: "Picture" });
  await expect(pictures.getByRole("group", { name: "Charity" })).toBeVisible();
  // Askwerk's photos (AWK-74): every thumbnail of the first topic has loaded.
  await expect.poll(() => pictures.getByRole("group", { name: "Charity" }).locator("img").evaluateAll((images) => images.every((image) => (image as HTMLImageElement).complete && (image as HTMLImageElement).naturalWidth > 0))).toBe(true);
  await page.screenshot({ path: info.outputPath("look-picture.png") });
  await settings.getByRole("button", { name: "Done" }).click();

  // Administration → Looks: the card opens its colour under More.
  await page.goto("/look");
  const preview = page.getByRole("region", { name: "Preview" });
  await preview.locator("[data-pick='surface']").click({ position: { x: 12, y: 12 } });
  await expect(page.locator("[data-field='surface']")).toBeVisible();
  await page.screenshot({ path: info.outputPath("looks-pick.png") });

  // Simple and Advanced (W45, AWK-71), switched from the person's menu. The choice is not stored here, so the other
  // flows signed in as the same owner keep their view.
  await page.route("**/api/app/account/view", (route) => route.fulfill({ status: 204 }));
  await page.goto("/");
  const side = page.getByRole("complementary", { name: "Navigation" });
  await side.getByRole("button", { name: /^Account: / }).click();
  await page.getByRole("menuitemradio", { name: "Simple" }).click();
  await expect(side.getByRole("link", { name: "Contacts" })).toHaveCount(0);
  await expect(side.getByRole("button", { name: /more places · Switch to Advanced/ })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: "Simple view" })).toBeVisible();
  await page.screenshot({ path: info.outputPath("view-simple.png") });
  await side.getByRole("button", { name: /Switch to Advanced/ }).click();
  await expect(side.getByRole("link", { name: "Contacts" })).toBeVisible();
  await page.screenshot({ path: info.outputPath("view-advanced.png") });

  // The survey itself: the language menu lists each language by its own name.
  await page.goto(`/s/${id}?preview`);
  await page.getByRole("button", { name: /: English$/ }).click();
  await page.screenshot({ path: info.outputPath("language-menu.png") });
  await page.getByRole("menuitemradio", { name: /Deutsch/ }).click();
  await expect(page.locator("html")).toHaveAttribute("lang", "de");
  await expect(page.getByRole("button", { name: /: Deutsch$/ })).toBeVisible();
});
