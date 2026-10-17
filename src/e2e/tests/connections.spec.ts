// SPDX-License-Identifier: AGPL-3.0-only
// Connections (W36, AWK-49): an owner connects a system in three steps, sees its key once, the system calls with it
// and the connection turns green; paused, its key stops working.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an owner connects a system, the system calls with its key, and pausing stops it", async ({ page, playwright }, info) => {
  test.skip(info.project.name !== "desktop-en", "Connections are checked on a desktop.");
  await page.goto("/");
  await page.getByLabel("Email", { exact: true }).fill(owner.email);
  await page.getByLabel("Password", { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys", exact: true })).toBeVisible();

  await goTo(page, "Connections");
  await expect(page.getByRole("heading", { level: 1, name: "Connections" })).toBeVisible();
  await page.getByRole("button", { name: "+ Connect a system" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Connect a system" })).toBeVisible();
  const wizard = page.getByRole("main");

  // 1 What it should do.
  await wizard.getByText("Get answers into my system").click();
  await wizard.getByText("Send people from my system").click();
  await page.screenshot({ path: info.outputPath("connect-goals.png") });
  await wizard.getByRole("button", { name: "Next" }).click();

  // 2 Details: no address, so the system fetches answers; a key for a nightly job.
  const name = `HR nightly ${Date.now()}`;
  await wizard.getByRole("textbox", { name: /Name/ }).fill(name);
  await wizard.getByText("API key", { exact: true }).click();
  await expect(wizard.getByRole("checkbox", { name: /Add and change people/ })).toBeChecked();
  await expect(wizard.getByRole("checkbox", { name: /Read surveys and answers/ })).toBeChecked();
  await page.screenshot({ path: info.outputPath("connect-details.png") });
  await wizard.getByRole("button", { name: "Create the connection" }).click();

  // 3 Test: the key once, then the system's first call turns it green.
  await expect(wizard.getByRole("heading", { name: "Credentials · shown once" })).toBeVisible();
  await expect(wizard.getByText("Waiting for the first call…")).toBeVisible();
  await wizard.getByRole("button", { name: "Show" }).click();
  const key = (await wizard.getByLabel("API key", { exact: true }).textContent())!.trim();
  expect(key).toMatch(/^awk_/);
  const system = await playwright.request.newContext({ baseURL: new URL(page.url()).origin, extraHTTPHeaders: { Authorization: `Bearer ${key}` } });
  expect((await system.get("/api/v1/surveys")).ok()).toBe(true);
  await expect(wizard.getByText("First call seen")).toBeVisible({ timeout: 15_000 });
  await wizard.getByRole("button", { name: "Hide" }).click();
  await page.screenshot({ path: info.outputPath("connect-test.png") });
  await wizard.getByRole("button", { name: "Done" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Connections" })).toBeVisible();

  // The list: it works.
  const row = page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: new RegExp(name) }) });
  await expect(row.getByText("Works")).toBeVisible();
  await page.screenshot({ path: info.outputPath("connections.png") });
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  // Paused, the key stops working; resumed, it works again.
  await row.getByRole("button", { name: `Open: ${name}` }).click();
  const panel = page.getByRole("dialog", { name });
  await panel.getByRole("button", { name: "Pause" }).click();
  await expect(panel.getByText("Paused", { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath("connection.png") });
  expect((await system.get("/api/v1/surveys")).status()).toBe(401);
  await panel.getByRole("button", { name: "Resume" }).click();
  await expect(panel.getByRole("button", { name: "Pause" })).toBeVisible();
  expect((await system.get("/api/v1/surveys")).ok()).toBe(true);
  await system.dispose();
});
