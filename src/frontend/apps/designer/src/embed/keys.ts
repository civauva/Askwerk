// SPDX-License-Identifier: AGPL-3.0-only
import type { KeyResult } from "@askwerk/app-client";

/**
 * A key result in one number, as the drop-in list and widget show it (W37): a scale as the share of its top two
 * values ("78 % satisfied (4–5)"), NPS as its score, a choice as its top option's share.
 */
export type KeySummary = { kind: "scale"; percent: number; range: string } | { kind: "nps"; score: number } | { kind: "choice"; percent: number; label: Record<string, string> | null };

export function keySummary(key: KeyResult): KeySummary | null {
  if (key.value === null) {
    return null;
  }

  if (key.type === "nps") {
    return { kind: "nps", score: key.value };
  }

  if (key.top !== null) {
    return { kind: "choice", percent: Math.round(key.value), label: key.top };
  }

  const values = key.spread.map((slice) => Number(slice.code)).filter(Number.isFinite);
  const top = Math.max(...values, key.max ?? 0, 2);
  const total = key.spread.reduce((sum, slice) => sum + slice.count, 0);
  const high = key.spread.filter((slice) => Number(slice.code) >= top - 1).reduce((sum, slice) => sum + slice.count, 0);

  return { kind: "scale", percent: total === 0 ? 0 : Math.round((high / total) * 100), range: `${top - 1}–${top}` };
}

/** How full the bar is, 0 to 1. */
export function keyShare(summary: KeySummary): number {
  return summary.kind === "nps" ? (summary.score + 100) / 200 : summary.percent / 100;
}
