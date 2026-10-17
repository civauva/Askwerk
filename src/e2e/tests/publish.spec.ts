// SPDX-License-Identifier: AGPL-3.0-only
// Publish (AWK-7, W12 B): after publishing, the link, QR code, embed code and PDF on one screen.
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
      questions: [{ id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } }],
    },
  ],
};

const text = {
  "desktop-en": { publish: "Publish", published: "Published. Responses are being collected.", qr: /^QR code for /, getEmbed: "Get embed code", embed: "Embed code", pdf: /^Download PDF/, pdfIn: /^PDF in / },
  "phone-de": { publish: "Veröffentlichen", published: "Veröffentlicht. Antworten werden gesammelt.", qr: /^QR-Code für /, getEmbed: "Code zum Einbetten holen", embed: "Code zum Einbetten", pdf: /^PDF herunterladen/, pdfIn: /^PDF auf / },
} as const;

test("an owner publishes and gets the link, QR code, embed code and PDF on one screen", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Surveys are published from the designer on a desktop.");
  const t = text[info.project.name as keyof typeof text];
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();

  const created = await page.request.post("/api/app/surveys", { data: { title: `Publish ${info.project.name}`, languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  await page.goto(`/surveys/${id}`);

  await page.getByRole("banner").getByRole("button", { name: t.publish, exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: t.publish, exact: true }).click();
  const dialog = page.getByRole("dialog", { name: t.published });
  await expect(dialog.getByRole("img", { name: t.qr })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath(`published-${info.project.name}.png`), fullPage: true });
  // The PDF from the "Download PDF ▾" menu, the embed code in its own dialog (W34).
  await dialog.getByRole("button", { name: t.pdf }).click();
  await expect(page.getByRole("menuitem", { name: t.pdfIn }).first()).toHaveAttribute("href", new RegExp(`^/api/app/surveys/${id}/pdf\\?language=`));
  await page.keyboard.press("Escape");
  await dialog.getByRole("button", { name: t.getEmbed, exact: true }).click();
  await expect(page.getByRole("dialog", { name: t.embed }).getByRole("textbox", { name: t.embed })).toHaveValue(/\/s\/embed\.js" data-survey="[23456789abcdefghjkmnpqrstuvwxyz]{8}"/);
});
