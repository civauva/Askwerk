// SPDX-License-Identifier: AGPL-3.0-only
// Drop-in components (W37, AWK-50): an owner sets the host look and gets the code; a host app's page shows a survey with
// <askwerk-survey>, in the host's colours, and hears when it is answered.
import { expect, test } from "@playwright/test";
import { goTo } from "./nav";
import { owner, readTwoFactorKey, totp } from "./owner";

// The host app's page is served by the test itself, which Chrome counts as a public website calling a local one; in
// production both are public, so the check that blocks that is off here.
const host = "http://portal.localhost";

test.use({ launchOptions: { args: ["--disable-features=LocalNetworkAccessChecks,PrivateNetworkAccessRespectPreflightResults,PrivateNetworkAccessSendPreflights,BlockInsecurePrivateNetworkRequests"] } });

test("a host app shows a survey with one tag, in its own look", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop-en", "Drop-in components are checked on a desktop.");
  await page.goto("/");
  await page.getByLabel("Email", { exact: true }).fill(owner.email);
  await page.getByLabel("Password", { exact: true }).fill(owner.password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("textbox", { name: "Digit 1 of 6" }).fill(totp(readTwoFactorKey()));
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Surveys", exact: true })).toBeVisible();
  const askwerk = new URL(page.url()).origin;

  // A published survey the host's website may show.
  const created = await page.request.post("/api/app/surveys", { data: { title: "Portal feedback", languages: ["en"], defaultLanguage: "en" } });
  const { id } = (await created.json()) as { id: string };
  const definition = { schemaVersion: 1, languages: ["en"], defaultLanguage: "en", pages: [{ id: "p1", questions: [{ id: "q1", variable: "SAT", type: "rating", text: { en: "How satisfied are you?" }, required: true, scale: { min: 1, max: 5 } }] }] };
  expect((await page.request.put(`/api/app/surveys/${id}/draft`, { data: { definition } })).ok()).toBe(true);
  expect((await page.request.post(`/api/app/surveys/${id}/publish`)).ok()).toBe(true);
  const links = (await (await page.request.get(`/api/app/surveys/${id}/links`)).json()) as { items?: { url: string }[] } | { url: string }[];
  const code = /\/s\/([^/?#]+)/.exec(JSON.stringify(links))![1]!;
  expect((await page.request.put("/api/app/embedding", { data: { origins: [host], designerOrigins: [host] } })).ok()).toBe(true);

  // Developers & API → Drop-in components: the host look, then the code.
  await goTo(page, "Developers & API");
  await page.getByRole("link", { name: "Drop-in components →" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Drop-in components" })).toBeVisible();
  await expect(page.getByText("portal.localhost", { exact: false }).first()).toBeVisible();
  await page.getByRole("textbox", { name: /Font/ }).fill("Georgia, serif");
  await page.getByRole("spinbutton", { name: /Corners/ }).fill("4");
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText(/Saved: the components follow this look/)).toBeVisible();
  await page.screenshot({ path: info.outputPath("drop-in-setup.png"), fullPage: true });

  // The host app: its own page, one script and one tag, its accent as a custom property.
  await page.route(`${host}/**`, (route) =>
    route.fulfill({
      contentType: "text/html",
      body: `<!doctype html><html lang="en"><head><title>Acme Portal</title><script src="${askwerk}/s/components.js" type="module"></script>
        <style>body{margin:0;font-family:Georgia,serif;background:#f3f5f9}header{background:#0f2a52;color:#fff;padding:14px 20px;font-weight:600}main{padding:22px 26px;max-width:720px}
        askwerk-survey{--askwerk-accent:#1456b8}</style></head>
        <body><header>Acme Portal</header><main><h1>How did we do?</h1><askwerk-survey survey="${code}" lang="en"></askwerk-survey><p id="thanks" hidden>Thank you!</p></main>
        <script>document.querySelector("askwerk-survey").addEventListener("askwerk:completed", () => { document.getElementById("thanks").hidden = false; });</script></body></html>`,
    }),
  );
  const problems: string[] = [];
  page.on("console", (message) => message.type() === "error" && problems.push(message.text()));
  page.on("pageerror", (error) => problems.push(error.message));
  page.on("requestfailed", (request) => problems.push(`${request.url()} ${request.failure()?.errorText ?? ""}`));
  await page.goto(`${host}/feedback`);
  await expect(page.locator("askwerk-survey iframe")).toBeAttached({ timeout: 10_000 }).catch(() => {
    throw new Error(`The script did not run: ${problems.join(" | ")}`);
  });
  const survey = page.locator("askwerk-survey").frameLocator("iframe");
  await survey.getByRole("button", { name: /^Start/ }).click({ timeout: 20_000 });
  await expect(survey.getByText("How satisfied are you?")).toBeVisible();
  const accent = await survey.locator(".run").evaluate((element) => getComputedStyle(element).getPropertyValue("--aw-signal").trim());
  expect(accent).toBe("#1456b8");
  await page.screenshot({ path: info.outputPath("drop-in-host.png"), fullPage: true });
});
