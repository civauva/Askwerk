// SPDX-License-Identifier: AGPL-3.0-only
// End-to-end flows against a running Askwerk (bin/test-e2e.sh starts one with docker compose).
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "tests",
  fullyParallel: true,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.ASKWERK_E2E_URL ?? "http://localhost:18080",
    trace: "retain-on-failure",
  },
  projects: [
    // Setup can run only once per installation, so the first-run flow goes first and the others depend on it.
    { name: "first-run", testMatch: /first-run\.spec\.ts/, use: { ...devices["Desktop Chrome"], locale: "en-GB" } },
    { name: "desktop-en", testIgnore: /first-run\.spec\.ts/, dependencies: ["first-run"], use: { ...devices["Desktop Chrome"], locale: "en-GB" } },
    { name: "phone-de", testIgnore: /first-run\.spec\.ts/, dependencies: ["first-run"], use: { ...devices["Pixel 7"], locale: "de-AT" } },
  ],
});
