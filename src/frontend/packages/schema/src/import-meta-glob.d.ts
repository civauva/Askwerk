// SPDX-License-Identifier: Apache-2.0
// Vite's import.meta.glob, used by the tests to load the shared golden corpus without Node types.
interface ImportMeta {
  glob<T>(pattern: string, options: { eager: true; import: "default" }): Record<string, T>;
}
