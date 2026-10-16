// SPDX-License-Identifier: AGPL-3.0-only
import { defineConfig } from "vitest/config";

export default defineConfig({
  // The first test of a file pays for jsdom and the first render; with every file running side by side (and the
  // backend tests next to them in bin/test-all.sh) that alone can pass 5 s, so a test gets 15 s. A test that is
  // slow on its own is still found: alone, these take well under a second.
  // The Developers hub (AWK-34) bundles the guides from docs/ at the repository root.
  server: { fs: { allow: ["../../..", "../../../../docs"] } },
  test: { environment: "jsdom", setupFiles: ["src/test-setup.ts"], testTimeout: 15_000 },
});
