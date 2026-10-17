// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyList, SurveysClient, SurveyStatus } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useEffect, useMemo, useState } from "react";
import { frontText } from "../surveys/ContentCard";
import { keyShare, keySummary } from "./keys";
import { embedMessages } from "./messages";

type EmbeddedListProps = {
  surveys: Pick<SurveysClient, "list">;
  locale: Locale;
  /** Results, Edit and New survey go to the host, which decides where they lead. */
  post: (event: { type: "askwerk:open"; surveyId: string; view: "results" | "designer" } | { type: "askwerk:create" }) => void;
};

/**
 * The survey list inside a host app (W37, <askwerk-surveys>): counts per status, each survey with its code, answers and
 * key result; Results, Edit and New survey are events for the host. No Askwerk bar or navigation.
 */
export function EmbeddedList({ surveys, locale, post }: EmbeddedListProps) {
  const t = useMemo(() => createTranslator(embedMessages, locale), [locale]);
  const [status, setStatus] = useState<SurveyStatus | null>(null);
  const [list, setList] = useState<SurveyList | null>(null);
  const [failed, setFailed] = useState(false);
  const day = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short" });

  useEffect(() => {
    surveys.list({ cards: true, limit: 50, ...(status === null ? {} : { status }) }).then(setList, () => setFailed(true));
  }, [surveys, status]);

  if (failed) {
    return <p role="alert">{t("failed")}</p>;
  }

  if (list === null) {
    return <p aria-busy="true">{t("starting")}</p>;
  }

  const filters: [SurveyStatus, "countPublished" | "countDrafts" | "countClosed", number][] = [
    ["published", "countPublished", list.counts.published],
    ["draft", "countDrafts", list.counts.draft],
    ["closed", "countClosed", list.counts.closed],
  ];

  return (
    <main className="embed-list" lang={locale}>
      <div className="embed-list__bar">
        <span className="embed-list__filters">
          {filters.map(([value, key, count]) => (
            <button key={value} type="button" className="embed-chip" aria-pressed={status === value} onClick={() => setStatus(status === value ? null : value)}>
              {t(key, { count })}
            </button>
          ))}
        </span>
        <button type="button" className="embed-button" onClick={() => post({ type: "askwerk:create" })}>
          {t("newSurvey")}
        </button>
      </div>
      {list.items.length === 0 ? (
        <p className="embed-muted">{t("emptyList")}</p>
      ) : (
        <table className="embed-table" aria-label={t("surveys")}>
          <thead>
            <tr>
              <th scope="col">{t("survey")}</th>
              <th scope="col">{t("status")}</th>
              <th scope="col">{t("answers")}</th>
              <th scope="col">{t("keyResult")}</th>
              <th scope="col">
                <span className="aw-visually-hidden">{t("actions")}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {list.items.map((item) => {
              const front = item.front;
              const key = front?.keys?.[0];
              const summary = key === undefined ? null : keySummary(key);
              const draft = item.status === "draft";

              return (
                <tr key={item.id}>
                  <td>
                    <b>{item.title}</b>
                    <span className="embed-muted">
                      {draft || front?.publishedAt == null
                        ? t("draftChanged", { date: day.format(new Date(item.updatedAt)) })
                        : t("publishedOn", { code: front.code ?? "", date: day.format(new Date(front.publishedAt)) })}
                    </span>
                  </td>
                  <td>
                    <span className="embed-chip" data-quiet={draft ? "" : undefined}>
                      {t(item.status === "published" ? "published" : item.status === "closed" ? "closed" : "draft")}
                    </span>
                  </td>
                  <td>{draft ? "—" : item.responses}</td>
                  <td className="embed-table__key">
                    {summary === null ? (
                      <span className="embed-muted">{t("noAnswersYet")}</span>
                    ) : (
                      <>
                        <span className="embed-bar" aria-hidden="true">
                          <i style={{ width: `${Math.round(keyShare(summary) * 100)}%` }} />
                        </span>
                        <span className="embed-muted">
                          {summary.kind === "nps"
                            ? `${t("nps")} ${summary.score > 0 ? "+" : ""}${summary.score}`
                            : summary.kind === "scale"
                              ? t("satisfiedShort", { value: summary.percent })
                              : `${frontText(summary.label, locale, front?.defaultLanguage ?? locale)} ${summary.percent} %`}
                        </span>
                      </>
                    )}
                  </td>
                  <td className="embed-table__actions">
                    {!draft && (
                      <button type="button" className="embed-button embed-button--ghost" aria-label={t("resultsOf", { title: item.title })} onClick={() => post({ type: "askwerk:open", surveyId: item.id, view: "results" })}>
                        {t("results")}
                      </button>
                    )}
                    <button type="button" className="embed-button embed-button--ghost" aria-label={t("editOf", { title: item.title })} onClick={() => post({ type: "askwerk:open", surveyId: item.id, view: "designer" })}>
                      {t("edit")}
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </main>
  );
}
