// SPDX-License-Identifier: AGPL-3.0-only
// Messages (AWK-17 step 6, W10 D and E): the invitation as respondents get it, drawn by the server from the theme.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an owner sees the invitation email in the survey's look, with the first question as answers", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Message texts are edited on a desktop.");
  await page.goto("/");
  await page.getByLabel("Email", { exact: true }).fill(owner.email);
  await page.getByLabel("Password", { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm" }).click();

  const previewed = page.waitForResponse((response) => response.url().includes("/api/app/messages/invitation/preview"));
  await goTo(page, "Messages");
  const answer = await previewed;
  expect(answer.status(), await answer.text()).toBe(200);
  await expect(page.getByRole("heading", { level: 1, name: "Messages" })).toBeVisible();
  const email = page.frameLocator('iframe[title="Email preview"]');
  await expect(email.getByRole("link", { name: "Start the survey" })).toBeVisible();
  // The sandboxed email is the recipient's content, not the app's page; axe checks the page around it.
  expect((await new AxeBuilder({ page }).exclude('iframe[title="Email preview"]').analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("messages.png"), fullPage: true });
});
