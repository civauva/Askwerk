// SPDX-License-Identifier: AGPL-3.0-only
// Members (AWK-30, W18): the owner invites a colleague with a link, the colleague joins with a new account, and the
// owner changes the role and removes them again.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an owner invites a colleague with a link, who joins; the owner changes the role and removes them", async ({ page, browser }, info) => {
  test.skip(info.project.name !== "desktop-en", "Members are managed from a desktop.");
  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();

  await goTo(page, "Members");
  await expect(page.getByRole("heading", { level: 1, name: "Members" })).toBeVisible();
  await page.getByRole("button", { name: "Invite members…" }).click();
  const invite = page.getByRole("dialog", { name: "Invite members" });
  await invite.getByRole("textbox", { name: "Email addresses, one per line" }).fill("ben.huber@acme.example");
  await page.screenshot({ path: info.outputPath("members-invite-desktop-en.png") });
  await invite.getByRole("button", { name: "Invite 1" }).click();
  // The e2e instance has no SMTP: the links are there to copy.
  await expect(invite.getByText("Email is not set up yet. Copy the links and pass them on.")).toBeVisible();
  await invite.getByRole("button", { name: "Done" }).click();

  const list = (await (await page.request.get("/api/app/members")).json()) as { invitations: { id: string; email: string }[] };
  const invitation = list.invitations.find((item) => item.email === "ben.huber@acme.example")!;
  const { link } = (await (await page.request.get(`/api/app/members/invitations/${invitation.id}/link`)).json()) as { link: string };

  const context = await browser.newContext({ ...info.project.use });
  const colleague = await context.newPage();
  await colleague.goto(new URL(link).pathname);
  await expect(colleague.getByRole("heading", { level: 1, name: /^Join .+ as Editor$/ })).toBeVisible();
  await colleague.getByRole("textbox", { name: "Name" }).fill("Ben Huber");
  await colleague.getByLabel("Password").fill("a long enough password");
  expect((await new AxeBuilder({ page: colleague }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await colleague.screenshot({ path: info.outputPath("join-desktop-en.png") });
  await colleague.getByRole("button", { name: /^Join / }).click();
  await expect(colleague.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();

  await page.reload();
  const ben = page.getByRole("row", { name: /Ben Huber/ });
  await expect(ben).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("members-desktop-en.png"), fullPage: true });
  await ben.getByRole("button", { name: /^Role of Ben Huber/ }).click();
  await page.getByRole("option", { name: "Viewer" }).click();
  await expect(ben.getByRole("button", { name: /^Role of Ben Huber/ })).toContainText("Viewer");
  await ben.getByRole("button", { name: "Remove Ben Huber…" }).click();
  await page.getByRole("dialog", { name: "Remove member" }).getByRole("button", { name: "Remove", exact: true }).click();
  await expect(page.getByRole("row", { name: /Ben Huber/ })).toHaveCount(0);

  // The removed colleague's session ends.
  expect((await colleague.request.get("/api/app/surveys")).status()).toBe(401);
  await context.close();
});
