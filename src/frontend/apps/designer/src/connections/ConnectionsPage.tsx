// SPDX-License-Identifier: AGPL-3.0-only
import "../admin/admin.css";
import "./connections.css";
import type { ConnectionItem, ConnectionsClient, SurveysClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ConnectionPanel } from "./ConnectionPanel";
import { ConnectWizard } from "./ConnectWizard";
import { connectionsMessages } from "./messages";
import { goalKeys, Health, permissionKeys, shortUrl, type T } from "./parts";

type ConnectionsPageProps = {
  client: ConnectionsClient;
  surveys: Pick<SurveysClient, "list">;
  locale: Locale;
  onNavigate: (path: string) => void;
  writeClipboard: (text: string) => Promise<void>;
  /** This Askwerk's address, for the code samples. */
  baseUrl?: string;
  now?: () => Date;
  pollMs?: number;
};

/** Initials for the mark: AC for Acme CRM. */
function markOf(name: string): string {
  return (
    name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0]!.toUpperCase())
      .join("") || "?"
  );
}

/**
 * Administration → Connections (W36, AWK-49): every system Askwerk works with, what it does and whether it works;
 * Connect a system in three steps; each opens to test, new secrets, pause or remove.
 */
export function ConnectionsPage({ client, surveys, locale, onNavigate, writeClipboard, baseUrl = location.origin, now = () => new Date(), pollMs }: ConnectionsPageProps) {
  const t = useMemo(() => createTranslator(connectionsMessages, locale), [locale]);
  const [items, setItems] = useState<ConnectionItem[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [connecting, setConnecting] = useState(false);
  const [opened, setOpened] = useState<ConnectionItem | null>(null);
  const [surveyList, setSurveyList] = useState<{ id: string; title: string }[]>([]);

  const reload = useCallback(() => client.list().then(setItems, () => setFailed(true)), [client]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const connect = () => {
    setConnecting(true);
    surveys.list({ limit: 100 }).then((list) => setSurveyList(list.items.map((survey) => ({ id: survey.id, title: survey.title }))), () => undefined);
  };
  const closed = (changed: boolean) => {
    setConnecting(false);
    setOpened(null);
    changed && void reload();
  };

  return (
    <main className="admin admin--wide connections" lang={locale}>
      <header className="connections__head">
        <div>
          <h1 className="connections__title">{t("title")}</h1>
          <p className="connection-muted">{t("intro")}</p>
        </div>
        <button type="button" className="designer-button designer-button--primary" onClick={connect}>
          {t("connect")}
        </button>
      </header>

      {failed && (
        <p role="alert" className="designer-error">
          {t("failed")}
        </p>
      )}
      {items === null && !failed && (
        <p role="status" aria-busy="true" className="connection-muted">
          …
        </p>
      )}
      {items !== null && items.length === 0 && <p className="connections__empty">{t("empty")}</p>}
      {items !== null && items.length > 0 && (
        <ul className="connections__list" aria-label={t("list")}>
          {items.map((connection) => (
            <li key={connection.id} className="connection-row" data-health={connection.health}>
              <span className="connection-row__mark" aria-hidden="true">
                {markOf(connection.name)}
              </span>
              <div className="connection-row__main">
                <h2>
                  {connection.name}
                  <span className="connection-chips">
                    {connection.goals.map((goal) => (
                      <span key={goal} className="connection-chip">
                        {goalKeys[goal].mark} {t(goalKeys[goal].chip)}
                      </span>
                    ))}
                  </span>
                </h2>
                <Lines t={t} connection={connection} />
              </div>
              <Health t={t} locale={locale} connection={connection} now={now()} />
              <button type="button" className="designer-button" aria-label={`${t("open")}: ${connection.name}`} onClick={() => setOpened(connection)}>
                {t("open")}
              </button>
            </li>
          ))}
        </ul>
      )}

      {connecting && <ConnectWizard t={t} client={client} surveys={surveyList} locale={locale} baseUrl={baseUrl} writeClipboard={writeClipboard} onNavigate={onNavigate} onClose={closed} {...(pollMs === undefined ? {} : { pollMs })} />}
      {opened !== null && <ConnectionPanel t={t} client={client} connection={opened} locale={locale} baseUrl={baseUrl} writeClipboard={writeClipboard} onClose={closed} now={now()} />}
    </main>
  );
}

/** What the connection does, one line per direction. */
function Lines({ t, connection }: { t: T; connection: ConnectionItem }) {
  const { webhook, calls, goals } = connection;

  return (
    <div className="connection-row__lines">
      {webhook !== null && (
        <p>
          <b>{t("weCall")}</b> · {webhook.events.join(", ")} → <span className="connection-mono">{shortUrl(webhook.url)}</span>
          {webhook.surveys.length > 0 && ` · ${webhook.surveys.map((survey) => survey.title).join(", ")}`}
        </p>
      )}
      {calls !== null && (
        <p>
          <b>{t("theyCall")}</b> · {calls.signIn === "oauth" ? t("oauthClient") : t("apiKey")} · {calls.permissions.map((permission) => t(permissionKeys[permission].short)).join(", ")}
        </p>
      )}
      {goals.includes("sign-in") && <p>{t("signInSetUp")}</p>}
      {goals.includes("embed") && <p>{t("embedSetUp")}</p>}
    </div>
  );
}
