// SPDX-License-Identifier: AGPL-3.0-only
// Administration → Looks (AWK-17 step 2, W10 A, W32): an owner starts a theme from a preset, changes the accent and finds it saved.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": { organisation: /^Organisation: /, look: "Looks", preset: "Start from Klar", newTheme: "New theme", accent: "Accent colour (hex)", saved: "Saved", more: "More", themes: "Themes", logoFile: "Logo file", logo: "Logo", preview: "Preview" },
  "phone-de": { organisation: /^Organisation: /, look: "Looks", preset: "Mit Klar beginnen", newTheme: "Neues Design", accent: "Akzentfarbe (Hex)", saved: "Gespeichert", more: "Mehr", themes: "Designs", logoFile: "Logo-Datei", logo: "Logo", preview: "Vorschau" },
} as const;

/** A 1 × 1 PNG; the server decodes it (SkiaSharp), which proves the image library runs in this build. */
const pixel = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==", "base64");

test("an owner gives the surveys a look: preset, accent colour, saved by itself", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();

  await goTo(page, t.look);
  await expect(page.getByRole("heading", { level: 1, name: t.look })).toBeVisible();

  // Both projects share one organisation: the first starts from a preset, the second adds a theme.
  const themes = page.getByRole("navigation", { name: t.themes });
  await page.locator(".look-side").waitFor();

  if (await themes.isVisible()) {
    await themes.getByRole("button", { name: t.newTheme }).click();
  }

  await page.getByRole("button", { name: t.preset }).click();
  await page.getByRole("textbox", { name: t.accent }).fill("#0b6e4f");
  await expect(page.getByRole("navigation", { name: t.themes }).locator(".look-swatch").last()).toHaveCSS("background-color", "rgb(11, 110, 79)");
  await expect(page.getByRole("status").filter({ hasText: t.saved })).toBeVisible();
  await page.getByLabel(t.logoFile).setInputFiles({ name: "logo.png", mimeType: "image/png", buffer: pixel });
  await expect(page.getByRole("region", { name: t.preview }).getByRole("img", { name: t.logo })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`look-${info.project.name}.png`), fullPage: true });
  await page.getByRole("button", { name: t.more, exact: true }).click();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`look-more-${info.project.name}.png`), fullPage: true });

  const saved = (await (await page.request.get("/api/app/themes")).json()) as { items: { tokens: { colours: { accent: string } } }[] };
  expect(saved.items.some((theme) => theme.tokens.colours.accent === "#0b6e4f")).toBe(true);
});
