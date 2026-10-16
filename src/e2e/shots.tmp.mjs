import { chromium } from "@playwright/test";
const [html, out] = process.argv.slice(2);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1500, height: 1000 } });
await page.goto("file:///" + html);
await page.waitForTimeout(1500);
const names = ["m5-a-settings-en", "m5-b-preview-panel-en", "m5-c-preview-phone-de", "m5-a-settings-en-dark"];
const screens = page.locator(".screen, .phone");
for (let index = 0; index < names.length; index++) {
  await screens.nth(index).screenshot({ path: `${out}/${names[index]}.png` });
}
await browser.close();
