// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type AppClient, type StatusResponse } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { Button, Skeleton, StatusPill } from "@askwerk/ui";
import { useCallback, useEffect, useState } from "react";
import { statusMessages } from "./messages";

type State =
  | { kind: "loading" }
  | { kind: "loaded"; status: StatusResponse }
  | { kind: "error"; traceId: string | undefined };

type StatusPageProps = {
  client: AppClient;
  initialLocale: Locale;
  now?: () => Date;
  retryDelayMs?: number;
  /** Under the signed-in app bar: no own bar and no language switch. */
  embedded?: boolean;
};

/**
 * The AWK-11 placeholder at "/": proves static hosting, App API, database and telemetry work together.
 * Replaced by the setup wizard later. Wireframe: design/AWK-11-runnable-skeleton/w2-placeholder-page.svg.
 */
export function StatusPage({ client, initialLocale, now = () => new Date(), retryDelayMs = 5000, embedded = false }: StatusPageProps) {
  const [locale, setLocale] = useState<Locale>(initialLocale);
  const [state, setState] = useState<State>({ kind: "loading" });
  const t = createTranslator(statusMessages, locale);

  const load = useCallback(() => {
    client.getStatus().then(
      (status) => setState({ kind: "loaded", status }),
      (error: unknown) => setState({ kind: "error", traceId: error instanceof ApiError ? error.traceId : undefined }),
    );
  }, [client]);

  useEffect(load, [load]);

  const isStarting = state.kind === "loaded" && state.status.database !== "ready";

  useEffect(() => {
    if (!isStarting) {
      return undefined;
    }

    const timer = setTimeout(load, retryDelayMs);

    return () => clearTimeout(timer);
  }, [isStarting, load, retryDelayMs, state]);

  return (
    <div className="status-page" lang={locale}>
      {!embedded && (
      <header className="status-page__bar">
        <span className="status-page__brand">Askwerk</span>
        <nav className="status-page__languages" aria-label="Language">
          <Button variant="quiet" pressed={locale === "de"} onClick={() => setLocale("de")}>
            {t("german")}
          </Button>
          <Button variant="quiet" pressed={locale === "en"} onClick={() => setLocale("en")}>
            {t("english")}
          </Button>
        </nav>
      </header>
      )}
      <main className="status-page__content">
        {state.kind === "loading" && (
          <div role="status" aria-busy="true" aria-label={t("loading")} className="status-page__stack">
            <Skeleton width="12rem" height="1.75rem" />
            <Skeleton width="16rem" height="0.875rem" />
            <Skeleton width="9rem" height="1.75rem" round />
          </div>
        )}
        {state.kind === "loaded" && state.status.database === "ready" && (
          <section className="status-page__stack">
            <h1>{t("runningTitle")}</h1>
            <p>{t("runningText")}</p>
            <StatusPill tone="positive">{t("databaseReady")}</StatusPill>
            <p className="status-page__meta">{versionLine(t("version", { version: state.status.version }), state.status.commit)}</p>
            <p className="status-page__meta">{t("started", { minutes: minutesSince(state.status.startedAt, now()) })}</p>
          </section>
        )}
        {isStarting && (
          <section className="status-page__stack">
            <h1>{t("startingTitle")}</h1>
            <p>{t("startingText")}</p>
            <StatusPill tone="negative">{t("databaseNotReady")}</StatusPill>
          </section>
        )}
        {state.kind === "error" && (
          <section className="status-page__stack">
            <h1>{t("errorTitle")}</h1>
            <p>{t("errorText")}</p>
            <div>
              <Button
                onClick={() => {
                  setState({ kind: "loading" });
                  load();
                }}
              >
                {t("tryAgain")}
              </Button>
            </div>
            {state.traceId && <p className="status-page__meta">{t("reference", { traceId: state.traceId })}</p>}
          </section>
        )}
      </main>
    </div>
  );
}

function versionLine(version: string, commit: string | null) {
  return commit ? `${version} · ${commit}` : version;
}

function minutesSince(startedAt: string, now: Date) {
  return Math.max(0, Math.floor((now.getTime() - new Date(startedAt).getTime()) / 60_000));
}
