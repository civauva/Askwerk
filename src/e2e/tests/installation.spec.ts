// SPDX-License-Identifier: AGPL-3.0-only
// This Askwerk (AWK-39, W30): a deleted organisation waits 30 days and can be restored, or its data removed at once once
// its short name is typed; the installation's settings are saved on their page.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an installation administrator restores a deleted organisation, removes its data, and saves the installation", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "The installation is managed from a desktop.");
  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();

  await goTo(page, "Organisations");
  await page.getByRole("button", { name: "New organisation" }).click();
  const create = page.getByRole("dialog", { name: "New organisation" });
  await create.getByRole("textbox", { name: "Name", exact: true }).fill("Test lab");
  await create.getByRole("textbox", { name: "Short name" }).fill("test-lab");
  await create.getByRole("textbox", { name: /First owner/ }).fill("clara@test-lab.example");
  await create.getByRole("button", { name: "Create" }).click();
  const grid = page.getByRole("grid", { name: "Organisations" });

  await grid.getByRole("button", { name: "Delete: Test lab" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete" }).click();
  await expect(grid.getByText("30 days left")).toBeVisible();
  await page.screenshot({ path: info.outputPath("organisations-deleted-desktop-en.png") });
  await grid.getByRole("button", { name: "Restore: Test lab" }).click();
  await expect(grid.getByText("30 days left")).toBeHidden();

  await grid.getByRole("button", { name: "Delete: Test lab" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete" }).click();
  await grid.getByRole("button", { name: "Remove data: Test lab" }).click();
  const remove = page.getByRole("dialog", { name: "Remove Test lab's data now?" });
  await expect(remove.getByText("Members and invitations")).toBeVisible();
  await remove.getByRole("textbox", { name: "Type the short name test-lab to confirm" }).fill("test-lab");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("organisations-remove-desktop-en.png") });
  await remove.getByRole("button", { name: "Remove data" }).click();
  await expect(page.getByText("The data of Test lab is removed.")).toBeVisible();
  await expect(grid.getByText("Test lab")).toBeHidden();

  await goTo(page, "Installation");
  await expect(page.getByRole("heading", { level: 1, name: "Installation" })).toBeVisible();
  await expect(page.getByText("Set in configuration")).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("installation-desktop-en.png"), fullPage: true });
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved", { exact: true })).toBeVisible();
});
