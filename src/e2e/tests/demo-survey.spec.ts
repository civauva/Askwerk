// SPDX-License-Identifier: AGPL-3.0-only
// The demo survey (AWK-23): every organisation has it, published, and respondents can open every page.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": { start: "Start the survey", page: "Page 1 of 3 · Your visit" },
  "phone-de": { start: "Umfrage starten", page: "Seite 1 von 3 · Ihr Besuch" },
} as const;

test("the demo survey is in the list and opens for respondents", async ({ page, browser }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByText("Demo: Customer satisfaction 2026")).toBeVisible();

  const list = (await (await page.request.get("/api/app/surveys?search=Demo")).json()) as { items: { id: string; title: string }[] };
  const demo = list.items.find((item) => item.title === "Demo: Customer satisfaction 2026")!;
  const context = await browser.newContext({ ...info.project.use });
  const respondent = await context.newPage();
  await respondent.goto(`/s/${demo.id}`);
  await respondent.getByRole("button", { name: t.start }).click();

  await expect(respondent.getByText(t.page)).toBeVisible();
  expect((await new AxeBuilder({ page: respondent }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await respondent.screenshot({ path: info.outputPath(`demo-page-1-${info.project.name}.png`), fullPage: true });
  // Answered: the chosen face and the slider filled up to the answer.
  await respondent.getByRole("group", { name: /(How clean|Wie sauber)/ }).locator("label").nth(3).click();
  await respondent.getByRole("slider").focus();
  await respondent.keyboard.press("ArrowRight");
  await respondent.keyboard.press("ArrowRight");
  await respondent.screenshot({ path: info.outputPath(`demo-page-1-answered-${info.project.name}.png`), fullPage: true });
  await context.close();
});
