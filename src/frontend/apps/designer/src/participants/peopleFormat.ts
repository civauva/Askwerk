// SPDX-License-Identifier: AGPL-3.0-only
// Dates and field names as the W35 screens show them: "2 Oct", "2 Oct 09:14", "Department".
import type { Locale } from "@askwerk/schema";

/** The formats of a language: British English for en, so "2 Oct", not "Oct 2". */
export function dateLocale(locale: Locale): string {
  return locale === "de" ? "de-DE" : "en-GB";
}

/** "2 Oct". */
export function dayText(locale: Locale, at: string, timeZone?: string): string {
  return new Intl.DateTimeFormat(dateLocale(locale), { day: "numeric", month: "short", ...(timeZone === undefined ? {} : { timeZone }) }).format(new Date(at));
}

/** "2 Oct 09:14". */
export function dayTimeText(locale: Locale, at: string, timeZone?: string): string {
  const time = new Intl.DateTimeFormat(dateLocale(locale), { hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...(timeZone === undefined ? {} : { timeZone }) }).format(new Date(at));

  return `${dayText(locale, at, timeZone)} ${time}`;
}

/** "23 Oct, 09:00". */
export function whenText(locale: Locale, at: string, timeZone?: string): string {
  return new Intl.DateTimeFormat(dateLocale(locale), { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...(timeZone === undefined ? {} : { timeZone }) }).format(new Date(at));
}

/** Today, yesterday, or the day. */
export function relativeDay(locale: Locale, at: string, words: { today: string; yesterday: string }, now: Date, timeZone?: string): string {
  const key = (moment: Date) => new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", ...(timeZone === undefined ? {} : { timeZone }) }).format(moment);
  const day = key(new Date(at));

  if (day === key(now)) {
    return words.today;
  }

  return day === key(new Date(now.getTime() - 86_400_000)) ? words.yesterday : dayText(locale, at, timeZone);
}

/** A field's name to show: its key with spaces and a capital, "customer_no" becomes "Customer no". */
export function fieldLabel(key: string): string {
  const text = key.replace(/_/g, " ").trim();

  return text.charAt(0).toLocaleUpperCase() + text.slice(1);
}

/** A link as people read it: without https://. */
export function shortLink(link: string): string {
  return link.replace(/^https?:\/\//, "");
}
