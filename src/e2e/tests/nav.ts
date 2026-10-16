// SPDX-License-Identifier: AGPL-3.0-only
// The app's sidebar (AWK-38, W29): on phones it is a drawer behind the menu button, opened here first.
import { expect, type Page } from "@playwright/test";

/** Opens the sidebar on phones (760 px and narrower); on desktops it is always there. */
export async function openSidebar(page: Page): Promise<void> {
  const side = page.getByRole("complementary", { name: "Navigation" });

  if ((page.viewportSize()?.width ?? 1440) <= 760) {
    const menu = page.getByRole("button", { name: /^(Menu|Menü)$/ });

    await menu.click();
    await expect(menu).toHaveAttribute("aria-expanded", "true");
  }

  await expect(side).toBeVisible();
}

/** Goes to a place of the sidebar by its name, e.g. "Members" or "Domains". */
export async function goTo(page: Page, name: string | RegExp): Promise<void> {
  await openSidebar(page);
  await page.getByRole("complementary", { name: "Navigation" }).getByRole("link", { name, exact: typeof name === "string" }).click();
}

/** Opens the menu of the person at the sidebar's foot. */
export async function openAccountMenu(page: Page, name: string | RegExp): Promise<void> {
  await openSidebar(page);
  await page.getByRole("button", { name }).click();
}
