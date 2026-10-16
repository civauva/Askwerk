// SPDX-License-Identifier: AGPL-3.0-only
// Survey settings → Collecting responses (AWK-29, W24): an editor sets a closing date and allows more than one response
// per browser; the bar says when the survey closes, and a respondent can answer again from the end page.
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const both = (de: string, en: string) => ({ de, en });

const definition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  welcome: { title: both("Messestand 2026", "Trade fair stand 2026"), text: both("Danke, dass Sie mitmachen.", "Thank you for taking part.") },
  endPage: { text: both("Danke! Ihre Antworten sind gespeichert.", "Thank you! Your answers are saved.") },
  pages: [{ id: "p1", questions: [{ id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } }] }],
};

const text = {
  "desktop-en": {
    set: "Set a closing date",
    field: "Closing date",
    save: "Save closing date",
    until: "Open until 1 July 2099, 18:00: the link takes responses.",
    status: /closes 1 Jul, 18:00/,
    one: "One response per person",
    saved: "Saved",
    start: "Start the survey",
    send: "Send",
    rating: /How satisfied/,
    end: "Thank you! Your answers are saved.",
    again: "Answer again",
  },
  "phone-de": {
    set: "Enddatum festlegen",
    field: "Enddatum",
    save: "Enddatum speichern",
    until: "Offen bis 1. Juli 2099, 18:00: Der Link nimmt Antworten an.",
    status: /endet 1\. Juli, 18:00/,
    one: "Eine Antwort pro Person",
    saved: "Gespeichert",
    start: "Umfrage starten",
    send: "Absenden",
    rating: /Wie zufrieden/,
    end: "Danke! Ihre Antworten sind gespeichert.",
    again: "Erneut antworten",
  },
} as const;

/** Signs the owner in and publishes the survey through the App API; returns its id and short link. */
async function publishedSurvey(page: Page, project: string): Promise<{ id: string; link: string }> {
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: `Collecting ${project}`, languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  const published = await page.request.post(`/api/app/surveys/${id}/publish`);
  expect(published.ok()).toBe(true);
  const { links } = (await published.json()) as { links: { url: string }[] };

  return { id, link: new URL(links[0]!.url).pathname };
}

test("an editor sets a closing date and allows more than one response; the runner follows", async ({ page, browser }, info) => {
  const t = text[info.project.name as keyof typeof text];
  const { id, link } = await publishedSurvey(page, info.project.name);

  await page.goto(`/surveys/${id}/survey-settings`);
  const main = page.getByRole("main");
  await main.getByRole("button", { name: t.set }).click();
  await main.getByLabel(t.field, { exact: true }).fill("2099-07-01T18:00");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`closing-date-entry-${info.project.name}.png`), fullPage: true });
  await main.getByRole("button", { name: t.save }).click();
  await expect(main.getByText(t.until)).toBeVisible();
  await expect(page.getByRole("banner")).toContainText(t.status);

  await main.getByRole("switch", { name: t.one }).click();
  await expect(main.getByRole("status")).toHaveText(t.saved);
  await expect(main.getByRole("switch", { name: t.one })).toHaveAttribute("aria-checked", "false");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`collecting-${info.project.name}.png`), fullPage: true });

  // A browser without the owner's cookies answers, then answers again from the end page (W24 C4).
  const context = await browser.newContext({ ...info.project.use });
  const respondent = await context.newPage();
  await respondent.goto(link);
  await respondent.getByRole("button", { name: t.start }).click();
  await respondent.getByRole("group", { name: t.rating }).locator("label").nth(3).click();
  await respondent.getByRole("button", { name: t.send }).click();
  await expect(respondent.getByRole("heading", { name: t.end })).toBeVisible();
  await respondent.screenshot({ path: info.outputPath(`answer-again-${info.project.name}.png`), fullPage: true });
  await respondent.getByRole("button", { name: t.again }).click();
  await expect(respondent.getByRole("group", { name: t.rating })).toBeVisible();
  await context.close();

  const responses = await page.request.get(`/api/app/surveys/${id}/summary`);
  expect(((await responses.json()) as { responses: number }).responses).toBe(2);
});
