// SPDX-License-Identifier: AGPL-3.0-only
// Results (AWK-24, W11): after one response, the owner sees the overview per question and the response itself.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const both = (de: string, en: string) => ({ de, en });

const definition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  pages: [
    {
      id: "p1",
      questions: [
        { id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } },
        { id: "q2", variable: "BEFORE", type: "yes-no", text: both("Waren Sie schon einmal hier?", "Have you been here before?"), required: true },
      ],
    },
    {
      id: "p2",
      questions: [{ id: "q3", variable: "IMPROVE", type: "long-text", text: both("Was können wir besser machen?", "What could we improve?"), required: false }],
    },
  ],
};

const text = {
  "desktop-en": { total: "1 response", card: "1 How satisfied are you?", responses: "Responses", panel: "Response", preview: /^Preview/, sidePanel: /In a side panel/, previewPanel: "Preview", closePreview: "Close preview", status: "Complete", export: "Export", dialog: "Export responses", download: "Download", close: "Close" },
  "phone-de": { total: "1 Antwort", card: "1 Wie zufrieden sind Sie?", responses: "Antworten", panel: "Antwort", preview: /^Vorschau/, sidePanel: /In einem Seitenbereich/, previewPanel: "Vorschau", closePreview: "Vorschau schließen", status: "Vollständig", export: "Exportieren", dialog: "Antworten exportieren", download: "Herunterladen", close: "Schließen" },
} as const;

test("an owner sees the overview per question and opens a response", async ({ page, browser }, info) => {
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: `Results ${info.project.name}`, languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  expect((await page.request.post(`/api/app/surveys/${id}/publish`)).ok()).toBe(true);

  // One respondent without an account answers through the runner's API.
  const context = await browser.newContext({ ...info.project.use });
  const respondent = context.request;
  expect((await respondent.post(`/api/app/run/${id}/response`, { data: { language: "de" } })).ok()).toBe(true);
  expect((await respondent.put(`/api/app/run/${id}/response/pages/p1`, { data: { answers: { q1: { number: 4 }, q2: { option: "1" } } } })).ok()).toBe(true);
  expect((await respondent.put(`/api/app/run/${id}/response/pages/p2`, { data: { answers: { q3: { text: "Kürzere Wartezeiten" } } } })).ok()).toBe(true);
  expect((await respondent.post(`/api/app/run/${id}/response/complete`)).ok()).toBe(true);
  await context.close();

  await page.goto(`/surveys/${id}/results`);
  await expect(page.getByRole("heading", { level: 2, name: t.total })).toBeVisible();
  await expect(page.getByRole("region", { name: t.card })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`results-overview-${info.project.name}.png`), fullPage: true });

  await page.getByRole("tab", { name: t.responses }).click();
  await page.getByRole("grid", { name: t.responses }).getByText(t.status, { exact: true }).click();
  const panel = page.getByRole("complementary", { name: t.panel });
  await expect(panel.getByText("Kürzere Wartezeiten")).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`results-responses-${info.project.name}.png`), fullPage: true });

  await panel.getByRole("button", { name: t.close }).click();

  // AWK-27: Preview is in the Results bar too.
  await page.getByRole("button", { name: t.preview }).click();
  await page.getByRole("menuitem", { name: t.sidePanel }).click();
  await expect(page.getByRole("complementary", { name: t.previewPanel }).frameLocator("iframe").getByRole("heading").first()).toBeVisible();
  await page.screenshot({ path: info.outputPath(`results-preview-${info.project.name}.png`), fullPage: true });
  await page.getByRole("button", { name: t.closePreview }).click();
  await page.getByRole("banner").getByRole("button", { name: t.export }).click();
  const dialog = page.getByRole("dialog", { name: t.dialog });
  await dialog.getByRole("button", { name: t.export }).click();
  const downloaded = page.waitForEvent("download");
  await dialog.getByRole("link", { name: t.download }).click();
  expect((await downloaded).suggestedFilename()).toBe(`results-${info.project.name}.xlsx`);
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`results-export-${info.project.name}.png`), fullPage: true });
});
