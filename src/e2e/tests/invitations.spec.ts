// SPDX-License-Identifier: AGPL-3.0-only
// Invitations (AWK-7, W12 C and D): personal links while email is not set up, the response kept with the person,
// and the unsubscribe page.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

const both = (de: string, en: string) => ({ de, en });

const definition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  pages: [{ id: "p1", questions: [{ id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } }] }],
};

test("an owner invites with personal links, the answer shows with the person, and the person can unsubscribe", async ({ page, browser }, info) => {
  test.skip(info.project.name !== "desktop-en", "Invitations are sent from a desktop; the unsubscribe page is checked there too.");
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: "Invitations", languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  expect((await page.request.post(`/api/app/surveys/${id}/publish`)).ok()).toBe(true);

  await page.goto(`/surveys/${id}/participants`);
  await page.getByRole("button", { name: /^\+ Add people/ }).click();
  await page.getByRole("menuitem", { name: /^Type or paste/ }).click();
  const dialog = page.getByRole("dialog", { name: "Invite participants" });
  await dialog.getByRole("textbox", { name: "Addresses, one per line" }).fill("anna.berger@acme.example\nnot-an-address");
  await expect(dialog.getByText("1 new · not an address: line 2 “not-an-address”")).toBeVisible();
  await dialog.getByRole("button", { name: "Next" }).click();
  await dialog.getByRole("button", { name: "Send 1 invitation" }).click();
  // The e2e instance has no SMTP: Askwerk offers personal links instead.
  await expect(dialog.getByRole("alert")).toContainText("Email is not set up yet");
  await dialog.getByRole("button", { name: "Make 1 personal link" }).click();
  await expect(dialog.getByRole("status")).toContainText("1 personal links made");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await dialog.getByRole("button", { name: "Close" }).click();
  await expect(page.getByRole("grid", { name: "Participants" })).toContainText("Link only");

  const participants = (await (await page.request.get(`/api/app/surveys/${id}/participants`)).json()) as { items: { link: string }[] };
  const link = new URL(participants.items[0]!.link);
  const token = link.searchParams.get("t")!;
  const context = await browser.newContext({ ...info.project.use });
  const respondent = await context.newPage();
  await respondent.goto(`${link.pathname}${link.search}`);
  await expect(respondent.getByRole("heading", { level: 1 })).toBeVisible();
  // The answers go through the runner's API with the token, as the runner sends them (checked in its own tests).
  expect((await context.request.post(`/api/app/run/${id}/response`, { data: { language: "en", token } })).ok()).toBe(true);
  expect((await context.request.put(`/api/app/run/${id}/response/pages/p1`, { data: { answers: { q1: { number: 4 } } } })).ok()).toBe(true);
  expect((await context.request.post(`/api/app/run/${id}/response/complete`)).ok()).toBe(true);

  await page.reload();
  await expect(page.getByRole("grid", { name: "Participants" })).toContainText("Answered");
  await page.screenshot({ path: info.outputPath("participants-desktop-en.png"), fullPage: true });

  await respondent.goto(`/s/u/${token}`);
  await respondent.getByRole("button", { name: "Unsubscribe" }).click();
  await expect(respondent.getByRole("status")).toContainText("will not invite a***@acme.example again.");
  expect((await new AxeBuilder({ page: respondent }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await respondent.screenshot({ path: info.outputPath("unsubscribe-desktop-en.png"), fullPage: true });
  await context.close();
});
