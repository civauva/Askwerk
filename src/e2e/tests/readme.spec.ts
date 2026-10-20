// SPDX-License-Identifier: AGPL-3.0-only
// The screenshots on the project's front page (README.md, docs/screenshots). Taken only on request:
// ASKWERK_README_SHOTS=1, then copy the PNGs from this test's output folder to docs/screenshots.
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

test("the screenshots for the README", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en" || process.env.ASKWERK_README_SHOTS !== "1", "Only on request, on a desktop.");
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/");
  await page.getByLabel("Email").fill(owner.email);
  await page.getByLabel("Password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys" })).toBeVisible();
  const created = await page.request.post("/api/app/samples/lodge", { data: { title: "Mountain lodge stay" } });
  const { id } = (await created.json()) as { id: string };
  const shot = (name: string) => page.screenshot({ path: info.outputPath(`${name}.png`) });
  const loaded = (images: ReturnType<typeof page.locator>) =>
    expect.poll(() => images.evaluateAll((all) => all.every((image) => (image as HTMLImageElement).complete && (image as HTMLImageElement).naturalWidth > 0))).toBe(true);

  // The designer: outline, canvas and inspector.
  await page.goto(`/surveys/${id}`);
  const cards = page.locator(".designer-card");
  await expect(cards.first()).toBeVisible();
  await cards.nth(2).click();
  await page.waitForTimeout(500);
  await shot("designer");

  // Look: the survey's look beside a live preview, with Askwerk's pictures.
  await page.goto(`/surveys/${id}/look`);
  const settings = page.getByRole("complementary", { name: "Look settings" });
  const frame = page.frameLocator("iframe[title='Survey preview']");
  await expect(frame.locator(".run-welcome")).toBeVisible();
  await settings.getByRole("button", { name: "Picture", exact: true }).click();
  const pictures = settings.getByRole("region", { name: "Picture" });
  await expect(pictures.getByRole("group", { name: "Charity" })).toBeVisible();
  await loaded(pictures.getByRole("group", { name: "Charity" }).locator("img"));
  await page.mouse.move(0, 0);
  await page.waitForTimeout(800);
  await shot("look");

  // What respondents see.
  await page.goto(`/s/${id}?preview`);
  await page.getByRole("button", { name: /^Start/ }).click();
  await expect(page.locator(".run-q").first()).toBeVisible();
  await page.waitForTimeout(800);
  await shot("respondent");

  // Results: 120 sample answers per question.
  await page.goto(`/surveys/${id}/results`);
  await expect(page.getByText(/responses?$/).first()).toBeVisible();
  await page.waitForTimeout(1500);
  await shot("results");

  // Sharing: link, QR code, email, website and paper on one screen.
  await page.goto(`/surveys/${id}/share`);
  await page.waitForTimeout(1500);
  await shot("share");
});
