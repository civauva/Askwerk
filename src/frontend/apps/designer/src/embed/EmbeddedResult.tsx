// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyKeyResult } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useEffect, useMemo, useState } from "react";
import { frontText } from "../surveys/ContentCard";
import { keyShare, keySummary } from "./keys";
import { embedMessages } from "./messages";

type EmbeddedResultProps = {
  load: () => Promise<SurveyKeyResult>;
  locale: Locale;
  /** How often the number refreshes; it is live. */
  refreshMs?: number;
};

/** One key result in a host app (W37, <askwerk-result>): the question, the number, its bar, how many answered; live. */
export function EmbeddedResult({ load, locale, refreshMs = 60_000 }: EmbeddedResultProps) {
  const t = useMemo(() => createTranslator(embedMessages, locale), [locale]);
  const [result, setResult] = useState<SurveyKeyResult | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const read = () => void load().then(setResult, () => setFailed(true));
    read();
    const timer = setInterval(read, refreshMs);

    return () => clearInterval(timer);
  }, [load, refreshMs]);

  if (failed) {
    return <p role="alert">{t("failed")}</p>;
  }

  if (result === null) {
    return <p aria-busy="true">{t("starting")}</p>;
  }

  const key = result.key;
  const summary = key === null ? null : keySummary(key);

  return (
    <main className="embed-result" lang={locale}>
      {key === null ? (
        <p className="embed-muted">{t("noKey")}</p>
      ) : (
        <>
          <h1 className="embed-result__question">{frontText(key.text, locale, locale)}</h1>
          {summary === null ? (
            <p className="embed-muted">{t("noAnswersYet")}</p>
          ) : (
            <>
              <p className="embed-result__value">
                {summary.kind === "nps" ? `${summary.score > 0 ? "+" : ""}${summary.score}` : `${summary.percent} %`}{" "}
                <small>{summary.kind === "nps" ? t("nps") : summary.kind === "scale" ? t("satisfied", { range: summary.range }) : frontText(summary.label, locale, locale)}</small>
              </p>
              <span className="embed-bar" aria-hidden="true">
                <i style={{ width: `${Math.round(keyShare(summary) * 100)}%` }} />
              </span>
            </>
          )}
          <span className="embed-muted">{key.answered === 1 ? t("answerLive") : t("answersLive", { count: key.answered })}</span>
        </>
      )}
    </main>
  );
}
