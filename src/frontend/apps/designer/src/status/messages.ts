// SPDX-License-Identifier: AGPL-3.0-only
import type { Catalogs } from "@askwerk/schema";

const en = {
  loading: "Loading",
  runningTitle: "Askwerk is running",
  runningText: "Surveys follow in the next step.",
  databaseReady: "Database ready",
  startingTitle: "Askwerk is starting",
  startingText: "The database is not reachable yet. This page retries every 5 seconds.",
  databaseNotReady: "Database not ready",
  errorTitle: "Something went wrong",
  errorText: "Askwerk did not answer. Check that the container is running.",
  tryAgain: "Try again",
  reference: "Reference: {traceId}",
  version: "Version {version}",
  started: "Started {minutes} min ago",
  german: "Deutsch",
  english: "English",
} as const;

export type StatusMessageKey = keyof typeof en;

export const statusMessages: Catalogs<StatusMessageKey> = {
  en,
  de: {
    loading: "Wird geladen",
    runningTitle: "Askwerk läuft",
    runningText: "Umfragen folgen im nächsten Schritt.",
    databaseReady: "Datenbank bereit",
    startingTitle: "Askwerk startet",
    startingText: "Die Datenbank ist noch nicht erreichbar. Diese Seite versucht es alle 5 Sekunden erneut.",
    databaseNotReady: "Datenbank nicht bereit",
    errorTitle: "Etwas ist schiefgelaufen",
    errorText: "Askwerk hat nicht geantwortet. Prüfen Sie, ob der Container läuft.",
    tryAgain: "Erneut versuchen",
    reference: "Referenz: {traceId}",
    version: "Version {version}",
    started: "Gestartet vor {minutes} Min.",
    german: "Deutsch",
    english: "English",
  },
};
