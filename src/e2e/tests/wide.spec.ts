// SPDX-License-Identifier: AGPL-3.0-only
// Wide screens (W43, AWK-63, AWK-64): the W42 screens at 1920 and 2560 px, and the Cards switch timed with real-sized
// data (nine sample surveys, about 900 answers more).
import { expect, test, type Page } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const samples = ["board", "cafe", "employees", "festival", "inspection", "kiosk", "lodge", "night", "pulse"];

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();
}

test("wide screens: the Look preview fills its room, Messages fits the window, Cards come quickly", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Measured once, on a desktop.");
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1920, height: 1080 });
  await signIn(page);

  const ids: string[] = [];
  for (const sample of samples) {
    const created = await page.request.post(`/api/app/samples/${sample}`, { data: { title: `Wide ${sample}` } });
    expect(created.ok()).toBe(true);
    ids.push(((await created.json()) as { id: string }).id);
  }

  // The cards' data, asked cold: what a click on Cards waits for at most.
  const asked = Date.now();
  const cold = await page.request.get("/api/app/surveys?limit=25&cards=true");
  const coldMs = Date.now() - asked;
  expect(cold.ok()).toBe(true);

  // List, then Cards as a person does it: point, click, see the cards.
  await page.goto("/");
  const list = page.getByRole("button", { name: "☰ List" });
  if ((await list.getAttribute("aria-pressed")) !== "true") {
    await list.click();
  }
  await expect(page.getByRole("grid", { name: "Surveys" })).toBeVisible();
  const cards = page.getByRole("button", { name: "▦ Cards" });
  await cards.hover();
  const clicked = Date.now();
  await cards.click();
  await expect(page.locator(".content-card").first()).toBeVisible();
  const switchMs = Date.now() - clicked;
  info.annotations.push({ type: "timing", description: `cards request cold ${coldMs} ms, switch ${switchMs} ms` });
  console.log(`[W43] cards request cold ${coldMs} ms, switch to Cards ${switchMs} ms`);
  expect(coldMs).toBeLessThan(1500);
  expect(switchMs).toBeLessThan(1000);
  await page.screenshot({ path: info.outputPath("wide-1920-cards.png") });

  for (const [width, height] of [[1920, 1080], [2560, 1300]] as const) {
    await page.setViewportSize({ width, height });

    // The survey's Look: the docked preview starts where the settings end and reaches the window's right edge.
    await page.goto(`/surveys/${ids[1]}/look`);
    const preview = page.locator(".designer-preview--docked");
    await expect(preview).toBeVisible();
    const side = (await page.locator(".sl-side").boundingBox())!;
    const docked = (await preview.boundingBox())!;
    expect(Math.abs(docked.x - (side.x + side.width))).toBeLessThanOrEqual(2);
    expect(Math.abs(docked.x + docked.width - width)).toBeLessThanOrEqual(2);
    await page.screenshot({ path: info.outputPath(`wide-${width}-look.png`) });

    // Messages: three columns to the bottom of the window, the page itself does not scroll.
    await page.goto("/messages");
    await expect(page.getByRole("button", { name: "Invitation" })).toBeVisible();
    const scrolls = await page.evaluate(() => document.scrollingElement!.scrollHeight - window.innerHeight);
    expect(scrolls).toBeLessThanOrEqual(1);
    await page.screenshot({ path: info.outputPath(`wide-${width}-messages.png`) });

    // Lists stop at 1,600 px; the row toolbar stays inside its row.
    await page.goto("/installation/organisations");
    const tools = page.getByRole("group", { name: "Acme GmbH" });
    await expect(tools).toBeVisible();
    const row = (await page.getByRole("row", { name: /Acme GmbH/ }).boundingBox())!;
    const bar = (await tools.boundingBox())!;
    expect(bar.y).toBeGreaterThan(row.y);
    expect(bar.y + bar.height).toBeLessThan(row.y + row.height);
    await page.screenshot({ path: info.outputPath(`wide-${width}-organisations.png`) });

    await page.goto("/contacts");
    await expect(page.getByRole("heading", { level: 1, name: "Contacts" })).toBeVisible();
    await page.screenshot({ path: info.outputPath(`wide-${width}-contacts.png`) });

    await page.goto("/");
    await expect(page.locator(".content-card").first()).toBeVisible();
    await page.screenshot({ path: info.outputPath(`wide-${width}-surveys.png`) });
  }
});
