// SPDX-License-Identifier: AGPL-3.0-only
// Dates and times as people type them, and as ISO strings for storage ("2026-10-14", "09:30"). No time zones:
// a date or time is what the person entered (agents/ui-design-system.md, Components).

/** English is written the European way (day first), as en-GB does. */
function region(locale: string): string {
  return locale === "en" ? "en-GB" : locale;
}

function iso(year: number, month: number, day: number): string | null {
  const date = new Date(Date.UTC(year, month - 1, day));

  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    return null;
  }

  return `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

/** Reads a typed date: "14.10.2026", "14/10/2026", "14-10-26" (day first) or ISO "2026-10-14"; null when it is no date. */
export function parseDate(text: string, _locale: string): string | null {
  const value = text.trim();
  const isoMatch = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(value);

  if (isoMatch) {
    return iso(Number(isoMatch[1]), Number(isoMatch[2]), Number(isoMatch[3]));
  }

  const dayFirst = /^(\d{1,2})[./-](\d{1,2})[./-](\d{2}|\d{4})$/.exec(value);

  if (!dayFirst) {
    return null;
  }

  const year = dayFirst[3]!.length === 2 ? 2000 + Number(dayFirst[3]) : Number(dayFirst[3]);

  return iso(year, Number(dayFirst[2]), Number(dayFirst[1]));
}

/** Writes an ISO date the way the language does: 14.10.2026 (de), 14/10/2026 (en). */
export function formatDate(value: string, locale: string): string {
  return new Intl.DateTimeFormat(region(locale), { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" }).format(toDate(value));
}

/** How a date is typed in the locale, shown as the field's placeholder (W46 A, AWK-72): "dd.mm.yyyy", "TT.MM.JJJJ". */
export function datePattern(locale: string): string {
  const [day, month, year] = locale.startsWith("de") ? ["TT", "MM", "JJJJ"] : ["dd", "mm", "yyyy"];

  return formatDate("2026-12-31", locale).replace("31", day).replace("12", month).replace("2026", year);
}

/** The ISO date as a Date at midnight UTC. */
export function toDate(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);

  return new Date(Date.UTC(year!, month! - 1, day!));
}

/** The Date (UTC day) as an ISO date. */
export function fromDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Moves an ISO date by days or whole months (the day is kept where the month allows). */
export function addDays(value: string, days: number): string {
  const date = toDate(value);
  date.setUTCDate(date.getUTCDate() + days);

  return fromDate(date);
}

export function addMonths(value: string, months: number): string {
  const date = toDate(value);
  const day = date.getUTCDate();
  date.setUTCDate(1);
  date.setUTCMonth(date.getUTCMonth() + months);
  const last = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate();
  date.setUTCDate(Math.min(day, last));

  return fromDate(date);
}

/** Reads a typed time on the 24-hour clock ("9:30", "0930", "9.30", "21") or with am/pm; null when it is no time. */
export function parseTime(text: string): string | null {
  const match = /^(\d{1,2})(?:[:.]?(\d{2}))?\s*(am|pm)?$/i.exec(text.trim());

  if (!match) {
    return null;
  }

  let hours = Number(match[1]);
  const minutes = Number(match[2] ?? 0);
  const half = match[3]?.toLowerCase();

  if (half !== undefined) {
    if (hours < 1 || hours > 12) {
      return null;
    }

    hours = (hours % 12) + (half === "pm" ? 12 : 0);
  }

  if (hours > 23 || minutes > 59) {
    return null;
  }

  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
}
