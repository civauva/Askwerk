// SPDX-License-Identifier: AGPL-3.0-only
// Participants, rich (W35): a table pasted from Excel with its columns mapped, External IDs and fields, one person,
// automatic reminders, and the people kept in Contacts.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { owner, readTwoFactorKey, totp } from "./owner";

test("an owner imports people with External IDs, opens one, turns on reminders and finds them in Contacts", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Importing and Contacts are checked on a desktop.");
  await page.goto("/");
  await page.getByLabel("Email", { exact: true }).fill(owner.email);
  await page.getByLabel("Password", { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys", exact: true })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: "Customer check-in", languages: ["en"], defaultLanguage: "en" } });
  const { id } = (await created.json()) as { id: string };
  const definition = {
    schemaVersion: 1,
    languages: ["en"],
    defaultLanguage: "en",
    pages: [{ id: "p1", questions: [{ id: "q1", variable: "SAT", type: "rating", text: { en: "How satisfied are you?" }, required: true, scale: { min: 1, max: 5 } }] }],
  };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  expect((await page.request.post(`/api/app/surveys/${id}/publish`)).ok()).toBe(true);

  // Import: a table pasted from Excel, the columns guessed.
  await page.goto(`/surveys/${id}/participants`);
  await page.getByRole("button", { name: /^\+ Add people/ }).click();
  await page.getByRole("menuitem", { name: /^Import a file or table/ }).click();
  const dialog = page.getByRole("dialog", { name: "Import people" });
  await dialog.getByRole("textbox", { name: /or paste a table/ }).fill("E-Mail-Adresse\tVorname\tNachname\tKundennummer\tAbteilung\nlena@acme.at\tLena\tGruber\tCRM-10442\tSales\nthomas@acme.at\tThomas\tHuber\tCRM-10871\tService\nsarah@acme.at\tSarah\tNovak\tCRM-11003\tSales\n");
  await dialog.getByRole("button", { name: "Read" }).click();
  await expect(dialog.getByRole("table", { name: "Which column is what" })).toBeVisible();
  await page.screenshot({ path: info.outputPath("import-columns.png") });
  await dialog.getByRole("button", { name: "Next" }).click();
  await expect(dialog.getByText("will be added")).toBeVisible();
  await dialog.getByRole("radio", { name: /Add them, send later/ }).check();
  await dialog.getByRole("checkbox", { name: "Also save them in Contacts" }).check();
  await dialog.getByRole("textbox", { name: "Group" }).fill("Key accounts");
  await page.screenshot({ path: info.outputPath("import-check.png") });
  await dialog.getByRole("button", { name: "Import 3 people" }).click();

  // The list: External IDs, the field, the funnel.
  const grid = page.getByRole("grid", { name: "Participants" });
  await expect(grid.getByText("lena@acme.at · CRM-10442")).toBeVisible();
  await expect(grid.getByRole("columnheader", { name: "abteilung" })).toBeVisible();
  await page.getByRole("switch", { name: "Automatic reminders" }).click();
  await expect(page.getByRole("switch", { name: "Automatic reminders" })).toHaveAttribute("aria-checked", "true");
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("participants-rich.png"), fullPage: true });

  // One person.
  await grid.getByText("lena@acme.at · CRM-10442").click();
  const person = page.getByRole("dialog", { name: "Lena Gruber" });
  await expect(person.getByRole("textbox", { name: "External ID" })).toHaveValue("CRM-10442");
  await expect(person.getByText("Invited", { exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath("person.png") });
  await page.keyboard.press("Escape");

  // Contacts: the same people, in the group.
  await page.goto("/contacts");
  const contacts = page.getByRole("grid", { name: "Contacts" });
  await expect(contacts.getByText("CRM-11003")).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Groups" }).getByRole("button", { name: /^Key accounts/ })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath("contacts.png"), fullPage: true });
});
