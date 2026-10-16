// SPDX-License-Identifier: AGPL-3.0-only
import type { UnsubscribeInfo } from "@askwerk/app-client";
import { createTranslator, type Catalogs } from "@askwerk/schema";
import { useEffect, useState } from "react";

const en = {
  loading: "Loading",
  title: "No more invitations from {organisation}",
  question: "{organisation} will not send survey invitations to {email} any more.",
  button: "Unsubscribe",
  done: "Done. {organisation} will not invite {email} again.",
  invalidTitle: "This link is not valid",
  invalid: "It may have been copied incompletely. Open it again from the email.",
  failed: "That did not work. Try again.",
} as const;

type Key = keyof typeof en;

const messages: Catalogs<Key> = {
  en,
  de: {
    loading: "Wird geladen",
    title: "Keine Einladungen mehr von {organisation}",
    question: "{organisation} schickt {email} dann keine Einladungen zu Umfragen mehr.",
    button: "Abmelden",
    done: "Erledigt. {organisation} lädt {email} nicht mehr ein.",
    invalidTitle: "Dieser Link ist nicht gültig",
    invalid: "Vielleicht wurde er nicht vollständig kopiert. Öffnen Sie ihn noch einmal aus der E-Mail.",
    failed: "Das hat nicht geklappt. Versuchen Sie es erneut.",
  },
};

type State = { kind: "loading" } | { kind: "invalid" } | { kind: "asking" | "saving" | "failed" | "done"; info: UnsubscribeInfo };

/**
 * The page behind the unsubscribe link in every invitation (AWK-7, W12 note b): one button, no account, in the
 * person's language. Afterwards the organisation never invites the address again.
 */
export function Unsubscribe({ load, confirm, initialLocale }: { load: () => Promise<UnsubscribeInfo>; confirm: () => Promise<void>; initialLocale: string }) {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    load().then(
      (info) => setState({ kind: info.done ? "done" : "asking", info }),
      () => setState({ kind: "invalid" }),
    );
  }, [load]);

  const language = state.kind === "loading" || state.kind === "invalid" ? initialLocale : (state.info.language ?? initialLocale);
  const t = createTranslator(messages, language === "de" ? "de" : "en");

  return (
    <div className="run" lang={language === "de" ? "de" : "en"}>
      <main className="run-page run-unsubscribe">
        {state.kind === "loading" && (
          <p role="status" aria-busy="true" className="run-muted">
            {t("loading")}
          </p>
        )}
        {state.kind === "invalid" && (
          <>
            <h1>{t("invalidTitle")}</h1>
            <p>{t("invalid")}</p>
          </>
        )}
        {state.kind !== "loading" && state.kind !== "invalid" && (
          <>
            <h1>{t("title", { organisation: state.info.organisation })}</h1>
            {state.kind === "done" ? (
              <p role="status">{t("done", { organisation: state.info.organisation, email: state.info.email })}</p>
            ) : (
              <>
                <p>{t("question", { organisation: state.info.organisation, email: state.info.email })}</p>
                <button
                  type="button"
                  className="run-button run-button--primary"
                  disabled={state.kind === "saving"}
                  onClick={() => {
                    const info = state.info;
                    setState({ kind: "saving", info });
                    confirm().then(
                      () => setState({ kind: "done", info }),
                      () => setState({ kind: "failed", info }),
                    );
                  }}
                >
                  {t("button")}
                </button>
                {state.kind === "failed" && <p role="alert">{t("failed")}</p>}
              </>
            )}
          </>
        )}
      </main>
    </div>
  );
}
