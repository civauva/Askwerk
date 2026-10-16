import { chromium } from "@playwright/test";
const [svg, out, width, height] = process.argv.slice(2);
const browser = await chromium.launch({ channel: "chromium" });
const page = await browser.newPage({ viewport: { width: Number(width), height: Number(height) } });
await page.goto("file:///" + svg);
await page.screenshot({ path: out });
await browser.close();
