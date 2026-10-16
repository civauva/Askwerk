// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "vitest/config";

export default defineConfig({
  // The corpus is loaded with import.meta.glob. Vitest's file-system module cache keeps the expanded glob of an
  // unchanged test file, so a newly added corpus case would be skipped silently. Off for this package.
  test: { fsModuleCache: false },
});
