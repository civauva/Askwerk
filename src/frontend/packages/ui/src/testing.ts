// SPDX-License-Identifier: AGPL-3.0-only
// Test helper shared by every app: runs axe-core on a rendered container (development only).
import axe from "axe-core";
import { expect } from "vitest";

export async function expectNoAxeViolations(container: Element): Promise<void> {
  // jsdom cannot compute colours, so contrast is checked in Playwright against real browsers instead. Nor can it run
  // axe inside iframes; a frame's own content (an email preview, the runner) is checked where it is rendered.
  const result = await axe.run(container, { iframes: false, rules: { "color-contrast": { enabled: false } } });
  const violations = result.violations.map((violation) => `${violation.id}: ${violation.help}`);

  expect(violations).toEqual([]);
}
