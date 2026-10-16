import { chromium } from "@playwright/test";
const [pdf, out] = process.argv.slice(2);
const browser = await chromium.launch({ channel: "chromium" });
const page = await browser.newPage({ viewport: { width: 900, height: 1250 } });
await page.goto("file:///" + pdf);
await page.waitForTimeout(2500);
await page.screenshot({ path: out });
await browser.close();
