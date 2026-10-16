// SPDX-License-Identifier: AGPL-3.0-only
// The Developers hub (AWK-34, W27): an owner opens it from the top bar, tries the public API with a trial key and
// downloads the Postman collection with this Askwerk's address.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an owner tries the public API with a trial key and gets the Postman collection", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "The Developers hub is a desktop tool.");

  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();

  await goTo(page, "Developers & API");
  await expect(page.getByRole("heading", { level: 1, name: "Askwerk for developers" })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("developers-start.png") });

  await page.getByRole("button", { name: "Create a trial key" }).click();
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.getByRole("region", { name: "Response" }).getByText("200", { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath("developers-try-it.png") });

  const postman = await page.request.get("/api/app/developers/postman");
  const collection = (await postman.json()) as { variable: { key: string; value: string }[] };
  const { baseUrl } = (await (await page.request.get("/api/app/developers")).json()) as { baseUrl: string };
  expect(collection.variable.find((variable) => variable.key === "baseUrl")?.value).toBe(baseUrl);

});
