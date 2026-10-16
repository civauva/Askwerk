// SPDX-License-Identifier: AGPL-3.0-only
// Surveys (AWK-15 step 3, W6): create one, edit it in the designer on the real API, and find it in the list again.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { goTo, openAccountMenu } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

const text = {
  "desktop-en": {
    signIn: "Sign in",
    email: "Email",
    password: "Password",
    code: "Digit 1 of 6",
    confirm: "Confirm",
    surveys: "Surveys",
    create: /^(New survey|Create the first survey)$/,
    dialog: "New survey",
    title: "Title (internal, not shown to respondents)",
    createButton: "Create",
    outline: "Outline",
    saved: "Saved",
    back: "All surveys",
    search: "Search surveys",
  },
  "phone-de": {
    signIn: "Anmelden",
    email: "E-Mail",
    password: "Passwort",
    code: "Ziffer 1 von 6",
    confirm: "Bestätigen",
    surveys: "Umfragen",
    create: /^(Neue Umfrage|Erste Umfrage anlegen)$/,
    dialog: "Neue Umfrage",
    title: "Titel (intern, nicht für Teilnehmende)",
    createButton: "Anlegen",
    outline: "Gliederung",
    saved: "Gespeichert",
    back: "Alle Umfragen",
    search: "Umfragen suchen",
  },
} as const;

test("the owner creates a survey, opens it in the designer and finds it in the list again", async ({ page }, info) => {
  const t = text[info.project.name as keyof typeof text];
  const title = `Customer satisfaction ${info.project.name}`;
  await page.goto("/");
  await page.getByLabel(t.email, { exact: true }).fill(owner.email);
  await page.getByLabel(t.password, { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: t.signIn, exact: true }).click();
  await page.getByRole("textbox", { name: t.code }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: t.confirm }).click();
  await expect(page.getByRole("heading", { level: 1, name: t.surveys, exact: true })).toBeVisible();

  await page.getByRole("button", { name: t.create }).click();
  const dialog = page.getByRole("dialog", { name: t.dialog });
  await dialog.getByLabel(t.title).fill(title);
  await dialog.getByRole("button", { name: t.createButton }).click();

  await expect(page.getByRole("navigation", { name: t.outline })).toBeVisible();
  await expect(page).toHaveURL(/\/surveys\/[0-9a-f-]{36}$/);
  await expect(page.getByText(title)).toBeVisible();
  // Back from a survey: the first row of the sidebar (AWK-38, W29 drill-in).
  await goTo(page, t.back);

  await expect(page.getByRole("heading", { level: 1, name: t.surveys, exact: true })).toBeVisible();
  await page.getByRole("searchbox", { name: t.search }).fill(info.project.name);
  await expect(page.getByRole("link", { name: title })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`surveys-${info.project.name}.png`), fullPage: true });
});
