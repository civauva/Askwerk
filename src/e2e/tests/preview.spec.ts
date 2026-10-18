// SPDX-License-Identifier: AGPL-3.0-only
// Survey settings and preview (AWK-22, W9, M5): an editor tries the draft in a side panel and in a new tab; nothing is stored.
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const both = (de: string, en: string) => ({ de, en });

const definition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  welcome: { title: both("Kundenzufriedenheit 2026", "Customer satisfaction 2026"), text: both("Danke, dass Sie mitmachen.", "Thank you for taking part.") },
  pages: [
    {
      id: "p1",
      title: both("Ihr Besuch", "Your visit"),
      questions: [{ id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } }],
    },
  ],
};

const text = {
  "desktop-en": { banner: "Preview · answers are not saved", start: "Start the survey", send: "Send", rating: /How satisfied/, end: "Thank you! Your answers are saved.", notFound: "Survey not found" },
  "phone-de": { banner: "Vorschau · Antworten werden nicht gespeichert", start: "Umfrage starten", send: "Absenden", rating: /Wie zufrieden/, end: "Danke! Ihre Antworten sind gespeichert.", notFound: "Umfrage nicht gefunden" },
} as const;

/** Signs the owner in and saves a draft through the App API, never publishing it; returns the survey's id. */
async function draftSurvey(page: Page, project: string): Promise<string> {
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: `Preview ${project}`, languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);

  return id;
}

test("an editor sets up the welcome page and tries the draft in a side panel", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "The designer is a desktop tool.");
  const id = await draftSurvey(page, info.project.name);

  await page.goto(`/surveys/${id}`);
  // The welcome page heads the page list (W23).
  await page.getByRole("navigation", { name: "Outline" }).getByRole("button", { name: /^Welcome page/ }).click();
  await expect(page.getByRole("textbox", { name: "Welcome title" })).toHaveValue("Customer satisfaction 2026");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("welcome.png") });

  // Create → Look opens on the editor beside the one Preview; the gallery of looks opens from it (W26, W33).
  await page.getByRole("navigation", { name: "Survey" }).getByRole("link", { name: "Look" }).click();
  const settings = page.getByRole("complementary", { name: "Look settings" });
  await settings.getByRole("button", { name: "Choose from gallery" }).click();
  await expect(page.getByRole("button", { name: "Use the look Konfetti" })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("look-gallery.png") });
  await page.getByRole("button", { name: "Use the look Konfetti" }).click();
  await expect(page.frameLocator('iframe[title="Survey preview"]').getByRole("heading", { level: 1, name: "Customer satisfaction 2026" })).toBeVisible();
  await settings.getByRole("tab", { name: "Parts" }).click();
  await page.getByRole("complementary", { name: "Look settings" }).getByRole("button", { name: /^Components/ }).click();
  await page.getByRole("complementary", { name: "Look settings" }).getByRole("button", { name: /^Choices/ }).click();
  await expect(page.frameLocator('iframe[title="Survey preview"]').locator(".run[data-pattern='confetti']")).toBeVisible();
  expect((await new AxeBuilder({ page }).exclude("iframe").analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("look-editor.png") });
  // Panorama (W28): the mountains behind the page, the questions on a frosted panel, the title over the picture.
  await settings.getByRole("button", { name: "Choose from gallery" }).click();
  await page.getByRole("button", { name: "Use the look Panorama" }).click();
  const panorama = page.frameLocator('iframe[title="Survey preview"]');
  await expect(panorama.locator(".run[data-pattern='mountains'][data-panel='frosted']")).toBeVisible();
  // Try (W46 A): the preview beside Look works like the survey.
  await page.getByRole("complementary", { name: "Preview" }).getByRole("button", { name: "Try" }).click();
  await panorama.getByRole("button", { name: "Start the survey" }).click();
  await expect(panorama.locator(".run-overtitle__title")).toHaveText("Customer satisfaction 2026");
  await page.screenshot({ path: info.outputPath("look-panorama.png") });

  // ⚙ Survey settings at the right end of the tabs (W25).
  await page.getByRole("navigation", { name: "Survey" }).getByRole("link", { name: "Survey settings" }).click();
  await expect(page.getByRole("heading", { level: 2, name: "Survey settings" })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("survey-settings.png") });
  await page.getByRole("navigation", { name: "Survey" }).getByRole("link", { name: "Questions" }).click();
  await expect(page.getByRole("navigation", { name: "Outline" })).toBeVisible();

  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await page.getByRole("menuitem", { name: "In a side panel" }).click();
  const runner = page.frameLocator('iframe[title="Survey preview"]');
  // The panel says it is a preview; the runner inside it does not repeat the banner (M5 B).
  await expect(page.getByRole("complementary", { name: "Preview" }).getByText(text["desktop-en"].banner)).toBeVisible();
  await expect(runner.getByRole("heading", { level: 1, name: "Customer satisfaction 2026" })).toBeVisible();
  await expect(runner.getByRole("note")).toHaveCount(0);
  await runner.getByRole("button", { name: "Start the survey" }).click();
  await runner.getByRole("group", { name: /How satisfied/ }).locator("label").nth(3).click();
  await page.screenshot({ path: info.outputPath("preview-panel.png") });

  // Approve exactly what Preview shows (AWK-28, W16): recorded with a note, shown next to Publish and in the audit log.
  const panel = page.getByRole("complementary", { name: "Preview" });
  await panel.getByRole("button", { name: "Approve…" }).click();
  const approve = page.getByRole("dialog", { name: "Approve this draft" });
  await approve.getByRole("textbox", { name: "Note (optional)" }).fill("checked with the works council");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("approve-dialog.png") });
  await approve.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(panel.getByText(/^✓ Approved by you · today /)).toBeVisible();
  await page.getByRole("banner").getByRole("button", { name: /^✓ Approved · / }).click();
  await expect(page.getByRole("dialog", { name: "Approvals" })).toContainText("checked with the works council");
  await page.screenshot({ path: info.outputPath("approvals.png") });
});

test("the draft in a new tab: checked like the real survey, a banner throughout, and only for editors", async ({ page, browser }, info) => {
  const t = text[info.project.name as keyof typeof text];
  const id = await draftSurvey(page, info.project.name);

  await page.goto(`/s/${id}?preview`);
  await expect(page.getByRole("note")).toHaveText(t.banner);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`preview-tab-${info.project.name}.png`), fullPage: true });
  await page.getByRole("button", { name: t.start }).click();
  await page.getByRole("button", { name: t.send }).click();
  await expect(page.getByRole("alert")).toBeVisible();
  await page.getByRole("group", { name: t.rating }).locator("label").nth(3).click();
  await page.getByRole("button", { name: t.send }).click();
  await expect(page.getByRole("heading", { name: t.end })).toBeVisible();
  await expect(page.getByRole("note")).toHaveText(t.banner);

  // Someone without the organisation's account sees no draft, with or without ?preview.
  const context = await browser.newContext({ ...info.project.use });
  const stranger = await context.newPage();
  await stranger.goto(`/s/${id}?preview`);
  await expect(stranger.getByRole("heading", { level: 1, name: t.notFound })).toBeVisible();
  await stranger.goto(`/s/${id}`);
  await expect(stranger.getByRole("heading", { level: 1, name: t.notFound })).toBeVisible();
  await context.close();
});
