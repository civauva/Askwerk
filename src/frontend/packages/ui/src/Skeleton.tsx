// SPDX-License-Identifier: AGPL-3.0-only

/** A placeholder block while content loads; skeletons instead of spinners (agents/ui-design-system.md). */
export function Skeleton({ width, height, round = false }: { width: string; height: string; round?: boolean }) {
  return <span aria-hidden="true" className={round ? "aw-skeleton aw-skeleton--round" : "aw-skeleton"} style={{ width, height }} />;
}
