// SPDX-License-Identifier: AGPL-3.0-only
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  server: {
    // During development the API runs on its own port; the browser still calls same-origin /api.
    // The runner (its own dev server) answers /s/, so the preview works in development too (AWK-22).
    // The Developers hub (AWK-34) bundles the guides from docs/ at the repository root.
    // The workspace (src/) stays allowed as before.
    fs: { allow: ["../../..", "../../../../docs"] },
    proxy: { "/api": "http://localhost:8080", "/health": "http://localhost:8080", "^/s/.*": "http://localhost:5174" },
  },
  build: { outDir: "dist", sourcemap: true },
});
