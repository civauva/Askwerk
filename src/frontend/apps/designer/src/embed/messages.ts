// SPDX-License-Identifier: AGPL-3.0-only
import type { Catalogs } from "@askwerk/schema";

const en = {
  starting: "Opening Askwerk…",
  "token-invalid": "The sign-in link has expired. Reload the page.",
  "embedding-off": "Embedding is switched off for this organisation.",
  "no-access": "Your account has no access here. Ask your administrator.",
  "survey-not-found": "This survey does not exist or was archived.",
  failed: "Askwerk could not be opened. Reload the page.",
  ending: "Your session ends in 2 minutes. Saved changes are kept.",
  ended: "Your session has ended. Reload the page to go on.",
} as const;

export type EmbedMessageKey = keyof typeof en;

export const embedMessages: Catalogs<EmbedMessageKey> = {
  en,
  de: {
    starting: "Askwerk wird geöffnet …",
    "token-invalid": "Die Anmeldung ist abgelaufen. Laden Sie die Seite neu.",
    "embedding-off": "Die Einbettung ist für diese Organisation ausgeschaltet.",
    "no-access": "Ihr Konto hat hier keinen Zugang. Wenden Sie sich an Ihre Administration.",
    "survey-not-found": "Diese Umfrage gibt es nicht oder sie wurde archiviert.",
    failed: "Askwerk konnte nicht geöffnet werden. Laden Sie die Seite neu.",
    ending: "Ihre Sitzung endet in 2 Minuten. Gespeichertes bleibt.",
    ended: "Ihre Sitzung ist beendet. Laden Sie die Seite neu, um weiterzumachen.",
  },
};
