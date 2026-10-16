// SPDX-License-Identifier: AGPL-3.0-only
// One layout language (AWK-41, W31): every page starts 32 px after the sidebar, left-aligned, at most 1,200 px for
// lists and 720 px for forms; the breadcrumb names the survey; the question inspector never scrolls sideways.
import { expect, test, type Page } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const lists = ["/", "/templates", "/members", "/domains", "/look", "/messages", "/organisation", "/sign-in-settings"];

/** Where the page's content starts and how wide it may get, against the sidebar's right edge. */
async function measure(page: Page) {
  return page.evaluate(() => {
    const side = document.querySelector("aside.shell-side")!.getBoundingClientRect();
    const main = document.querySelector("main")!;
    const style = getComputedStyle(main);

    return { left: Math.round(main.getBoundingClientRect().left - side.right), padding: style.paddingLeft, top: style.paddingTop, maxWidth: style.maxWidth, margin: style.marginLeft };
  });
}

test("every page starts at the same place, survey pages show a breadcrumb, and the inspector fits", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "The layout is measured once, on a desktop.");
  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();

  for (const path of lists) {
    await page.goto(path);
    await expect(page.locator("main").first()).toBeVisible();
    expect({ path, ...(await measure(page)) }).toEqual({ path, left: 0, padding: "32px", top: "28px", maxWidth: "1200px", margin: "0px" });
  }

  // A survey with a picture choice: its pages, the breadcrumb, and the inspector.
  const created = await page.request.post("/api/app/surveys", { data: { title: "Layout check", languages: ["en"], defaultLanguage: "en" } });
  const { id } = (await created.json()) as { id: string };
  const definition = {
    schemaVersion: 1,
    languages: ["en"],
    defaultLanguage: "en",
    pages: [{ id: "p1", questions: [{ id: "q1", variable: "PLACE", type: "single", text: { en: "Where do you feel at home?" }, required: false, options: [1, 2, 3, 4].map((n) => ({ code: String(n), label: { en: `A long option label number ${n} that needs room` } })) }] }],
  };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);

  await page.goto(`/surveys/${id}/survey-settings`);
  await expect(page.getByRole("navigation", { name: "You are here" })).toContainText("Layout check");
  expect(await measure(page)).toEqual({ left: 0, padding: "32px", top: "28px", maxWidth: "720px", margin: "0px" });
  await page.screenshot({ path: info.outputPath("layout-survey-settings.png") });

  await page.goto(`/surveys/${id}/share`);
  await expect(page.locator("main").first()).toBeVisible();
  expect(await measure(page)).toEqual({ left: 0, padding: "32px", top: "28px", maxWidth: "1200px", margin: "0px" });
  await page.getByRole("navigation", { name: "You are here" }).getByRole("link", { name: "Layout check" }).click();
  await expect(page.getByRole("navigation", { name: "Outline" })).toBeVisible();

  await page.getByRole("navigation", { name: "Outline" }).getByRole("button", { name: /Where do you feel at home/ }).click();
  const inspector = page.locator(".designer-inspector");
  await expect(inspector.getByRole("button", { name: "Add a picture for option 1" })).toBeVisible();
  const fits = await inspector.evaluate((element) => element.scrollWidth <= element.clientWidth);
  expect(fits).toBe(true);
  await page.screenshot({ path: info.outputPath("layout-inspector.png") });
});
