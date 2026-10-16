// SPDX-License-Identifier: AGPL-3.0-only
// The runner in a theme (AWK-17 step 3, W10 E): logo, header image and colours from the survey's theme.
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { crc32, deflateSync } from "node:zlib";
import { owner, readTwoFactorKey, totp } from "./owner";

const both = (de: string, en: string) => ({ de, en });

/** A PNG with a soft diagonal gradient, made here so the test needs no image files. */
function gradientPng(width: number, height: number, from: [number, number, number], to: [number, number, number]): Buffer {
  const raw = Buffer.alloc((width * 3 + 1) * height);

  for (let y = 0; y < height; y++) {
    raw[y * (width * 3 + 1)] = 0;

    for (let x = 0; x < width; x++) {
      const share = (x / width + y / height) / 2;
      const offset = y * (width * 3 + 1) + 1 + x * 3;
      [0, 1, 2].forEach((channel) => (raw[offset + channel] = Math.round(from[channel]! + (to[channel]! - from[channel]!) * share)));
    }
  }

  const chunk = (type: string, data: Buffer) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const typed = Buffer.concat([Buffer.from(type, "ascii"), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(typed));
    return Buffer.concat([length, typed, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 2;

  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk("IHDR", header), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

const definition = {
  schemaVersion: 1,
  languages: ["de", "en"],
  defaultLanguage: "de",
  welcome: { title: both("Ihre Meinung zählt", "Your opinion counts"), text: both("Drei kurze Fragen zu Ihrem Besuch.", "Three short questions about your visit.") },
  pages: [
    {
      id: "p1",
      title: both("Ihr Besuch", "Your visit"),
      questions: [
        { id: "q1", variable: "SAT", type: "rating", text: both("Wie zufrieden sind Sie?", "How satisfied are you?"), required: true, scale: { min: 1, max: 5 } },
        { id: "q2", variable: "AGAIN", type: "yes-no", text: both("Kommen Sie wieder?", "Will you come back?"), required: false },
      ],
    },
  ],
};

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByLabel(/^(Email|E-Mail)$/).fill(owner.email);
  await page.getByLabel(/^(Password|Passwort)$/).fill(owner.password);
  await page.getByRole("button", { name: /^(Sign in|Anmelden)$/ }).click();
  await page.getByRole("textbox", { name: /^(Digit 1 of 6|Ziffer 1 von 6)$/ }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: /^(Confirm|Bestätigen)$/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^(Surveys|Umfragen)$/ })).toBeVisible();
}

async function upload(page: Page, name: string, buffer: Buffer): Promise<string> {
  const answer = await page.request.post("/api/app/assets", { multipart: { kind: "image", file: { name, mimeType: "image/png", buffer } } });
  expect(answer.ok()).toBe(true);

  return ((await answer.json()) as { id: string }).id;
}

test("respondents see the survey in its theme: logo, header image, colours", async ({ page, browser }, info) => {
  await signIn(page);
  const logo = await upload(page, "logo.png", gradientPng(240, 60, [11, 110, 79], [20, 60, 50]));
  const header = await upload(page, "header.png", gradientPng(1600, 500, [214, 233, 223], [11, 110, 79]));
  const tokens = {
    schemaVersion: 1,
    colours: { accent: "#0b6e4f", onAccent: "#ffffff", background: "#f3f7f5", surface: "#ffffff", text: "#13231d", muted: "#4f625a", line: "#d3e0da" },
    fonts: { heading: { family: "IBM Plex Serif" }, body: { family: "IBM Plex Sans" } },
    scale: "m",
    spacing: "normal",
    corners: "rounded",
    buttons: "filled",
    controls: "boxes",
    progress: "bar",
    layout: "header-image",
    logo: { asset: logo },
    headerImage: { asset: header, focalX: 0.5, focalY: 0.5 },
  };
  const theme = await page.request.post("/api/app/themes", { data: { name: `Green ${info.project.name}`, tokens } });
  expect(theme.ok()).toBe(true);
  const { id: themeId } = (await theme.json()) as { id: string };
  const created = await page.request.post("/api/app/surveys", { data: { title: `Themed ${info.project.name}`, languages: ["de", "en"], defaultLanguage: "de" } });
  const { id } = (await created.json()) as { id: string };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  expect((await page.request.put(`/api/app/surveys/${id}/theme`, { data: { themeId } })).ok()).toBe(true);
  expect((await page.request.post(`/api/app/surveys/${id}/publish`)).ok()).toBe(true);

  const context = await browser.newContext({ ...info.project.use });
  const respondent = await context.newPage();
  await respondent.goto(`/s/${id}`);
  await expect(respondent.getByRole("heading", { level: 1 })).toBeVisible();
  await expect(respondent.getByRole("img", { name: "Acme GmbH" })).toBeVisible();
  await expect(respondent.locator(".run-hero img")).toBeVisible();
  const start = respondent.getByRole("button", { name: /^(Start the survey|Umfrage starten)$/ });
  await expect(start).toHaveCSS("background-color", "rgb(11, 110, 79)");
  expect((await new AxeBuilder({ page: respondent }).analyze()).violations.map((violation) => violation.id)).toEqual([]);
  await respondent.screenshot({ path: info.outputPath(`themed-welcome-${info.project.name}.png`), fullPage: true });

  await start.click();
  await respondent.getByRole("group", { name: /(How satisfied|Wie zufrieden)/ }).locator("label").nth(3).click();
  await respondent.screenshot({ path: info.outputPath(`themed-page-${info.project.name}.png`), fullPage: true });
  await context.close();
});
