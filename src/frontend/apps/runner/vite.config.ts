// SPDX-License-Identifier: AGPL-3.0-only
// The runner is served at /s/{survey} by the same host as the designer (AWK-6); its files live under /s/.
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  base: "/s/",
  plugins: [react()],
  server: {
    port: 5174,
    proxy: { "/api": "http://localhost:8080" },
  },
  build: { outDir: "dist", sourcemap: true },
});
