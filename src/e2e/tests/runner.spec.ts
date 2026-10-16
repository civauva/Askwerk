// SPDX-License-Identifier: AGPL-3.0-only
// The runner (AWK-6, W8, M4): a published survey answered by a respondent without an account, phone DE and desktop EN.
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const both = (de: string, en: string) => ({ de, en });

const definition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  welcome: { title: both("Kundenzufriedenheit", "Customer satisfaction"), text: both("Danke, dass Sie mitmachen.", "Thank you for taking part.") },
  endPage: { text: both("Danke! Ihre Antworten sind gespeichert.", "Thank you! Your answers are saved.") },
  pages: [
    {
      id: "p1",
      title: both("Ihr Besuch", "Your visit"),
      questions: [
        { id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } },
        { id: "q2", variable: "BEFORE", type: "yes-no", text: both("Waren Sie schon einmal hier?", "Have you been here before?"), required: true },
      ],
    },
    {
      id: "p2",
      questions: [
        { id: "q3", variable: "IMPROVE", type: "long-text", text: both("Was können wir besser machen?", "What could we improve?"), required: false },
        {
          id: "q4",
          variable: "REASON",
          type: "single",
          text: both("Was war der Hauptgrund Ihres Besuchs?", "What was the main reason for your visit?"),
          required: false,
          options: [
            { code: "1", label: both("Einkauf", "Shopping") },
            { code: "2", label: both("Beratung", "Advice") },
          ],
          other: { code: "99", label: both("Sonstiges, bitte angeben:", "Other, please specify:"), variable: "REASON_OTHER" },
        },
      ],
    },
  ],
};

const text = {
  "desktop-en": { start: "Start the survey", next: "Next", send: "Send", missing: "2 answers missing", rating: /How satisfied/, yes: /^Yes/, improve: "What could we improve?", end: "Thank you! Your answers are saved.", already: "You have already answered" },
  "phone-de": { start: "Umfrage starten", next: "Weiter", send: "Absenden", missing: "2 Antworten fehlen noch", rating: /Wie zufrieden/, yes: /^Ja/, improve: "Was können wir besser machen?", end: "Danke! Ihre Antworten sind gespeichert.", already: "Sie haben bereits geantwortet" },
} as const;

/** Signs the owner in and publishes the survey through the App API; returns the survey's short link. */
async function publishedSurvey(page: Page, project: string): Promise<string> {
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: `Runner ${project}`, languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  const published = await page.request.post(`/api/app/surveys/${id}/publish`);
  expect(published.ok()).toBe(true);
  // The short link (AWK-7): /s/{code}.
  const { links } = (await published.json()) as { links: { url: string }[] };
  const link = new URL(links[0]!.url).pathname;
  expect(link).toMatch(/^\/s\/[23456789abcdefghjkmnpqrstuvwxyz]{8}$/);

  return link;
}

test("a respondent answers a published survey: welcome, missing answers, pages, thank you", async ({ page, browser }, info) => {
  const t = text[info.project.name as keyof typeof text];
  const link = await publishedSurvey(page, info.project.name);
  // A separate browser context: the respondent has no account and no cookies of the owner.
  const context = await browser.newContext({ ...info.project.use });
  const respondent = await context.newPage();

  await respondent.goto(link);
  await expect(respondent.getByRole("heading", { level: 1 })).toBeVisible();
  expect((await new AxeBuilder({ page: respondent }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await respondent.getByRole("button", { name: t.start }).click();

  await respondent.getByRole("button", { name: t.next }).click();
  await expect(respondent.getByRole("alert")).toContainText(t.missing);
  expect((await new AxeBuilder({ page: respondent }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await respondent.screenshot({ path: info.outputPath(`runner-errors-${info.project.name}.png`), fullPage: true });

  const rating = respondent.getByRole("group", { name: t.rating });
  await rating.locator("label").nth(3).click();
  await respondent.getByRole("group", { name: /hier|here/ }).locator("label").filter({ hasText: t.yes }).click();
  await respondent.getByRole("button", { name: t.next }).click();

  await respondent.getByRole("textbox", { name: t.improve }).fill("More parking spaces");
  // "Other, please specify" sits inside its option with room above and below, not glued to its edges (AWK-20).
  const gaps = await respondent.locator(".run-choice__other").evaluate((field) => {
    const inner = field.getBoundingClientRect();
    const outer = field.closest(".run-choice")!.getBoundingClientRect();
    return { top: inner.top - outer.top, bottom: outer.bottom - inner.bottom, right: outer.right - inner.right };
  });
  expect(gaps.top).toBeGreaterThanOrEqual(6);
  expect(gaps.bottom).toBeGreaterThanOrEqual(6);
  expect(gaps.right).toBeGreaterThanOrEqual(8);
  await respondent.screenshot({ path: info.outputPath(`runner-page-${info.project.name}.png`), fullPage: true });
  await respondent.getByRole("button", { name: t.send }).click();

  await expect(respondent.getByRole("heading", { name: t.end })).toBeVisible();

  // The same browser comes back to "already answered", not to a new response (one response per person, W24 C3).
  await respondent.reload();
  await expect(respondent.getByRole("heading", { name: t.already })).toBeVisible();
  await context.close();
});
