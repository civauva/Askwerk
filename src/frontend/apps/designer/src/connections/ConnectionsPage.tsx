// SPDX-License-Identifier: AGPL-3.0-only
import "../admin/admin.css";
import "./connections.css";
import type { ConnectionItem, ConnectionsClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ConnectionPanel } from "./ConnectionPanel";
import { connectionsMessages } from "./messages";
import { Flow, goalKeys, Health, tileOf } from "./parts";

type ConnectionsPageProps = {
  client: ConnectionsClient;
  locale: Locale;
  onNavigate: (path: string) => void;
  writeClipboard: (text: string) => Promise<void>;
  /** This Askwerk's address, for the code samples. */
  baseUrl?: string;
  now?: () => Date;
};

/**
 * Administration → Connections (W36, AWK-49): one row per system: what it does, how the two talk, and whether it
 * works, from deliveries and calls. Open shows the details, a test, new secrets, pause and remove.
 */
export function ConnectionsPage({ client, locale, onNavigate, writeClipboard, baseUrl = location.origin, now = () => new Date() }: ConnectionsPageProps) {
  const t = useMemo(() => createTranslator(connectionsMessages, locale), [locale]);
  const [items, setItems] = useState<ConnectionItem[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [opened, setOpened] = useState<ConnectionItem | null>(null);

  const reload = useCallback(() => client.list().then(setItems, () => setFailed(true)), [client]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const closed = (changed: boolean) => {
    setOpened(null);
    changed && void reload();
  };

  return (
    <main className="admin admin--wide connections" lang={locale}>
      <header className="connections__head">
        <div>
          <h1 className="connections__title">{t("title")}</h1>
          <p className="connections__lead">{t("intro")}</p>
        </div>
        <button type="button" className="designer-button designer-button--primary" onClick={() => onNavigate("/connections/new")}>
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
          {items.map((connection) => {
            const tile = tileOf(connection);

            return (
              <li key={connection.id} className="connection-row" data-health={connection.health}>
                <span className="connection-row__tile" style={{ background: tile.colour }} aria-hidden="true">
                  {tile.label}
                </span>
                <div className="connection-row__name">
                  <h2>{connection.name}</h2>
                  <span className="connection-chips">
                    {connection.goals.map((goal) => (
                      <span key={goal} className="connection-chip">
                        {goalKeys[goal].mark} {t(goalKeys[goal].chip)}
                      </span>
                    ))}
                  </span>
                </div>
                <Flow t={t} connection={connection} />
                <Health t={t} locale={locale} connection={connection} now={now()} bars />
                <button type="button" className="designer-button connection-row__open" aria-label={`${t("open")}: ${connection.name}`} onClick={() => setOpened(connection)}>
                  {t("open")}
                </button>
              </li>
            );
          })}
        </ul>
      )}
      <p className="connections__note">
        {t("noteBefore")}{" "}
        <a
          href="/api-keys"
          onClick={(event) => {
            event.preventDefault();
            onNavigate("/api-keys");
          }}
        >
          {t("noteKeys")}
        </a>{" "}
        {t("noteAfter")}
      </p>

      {opened !== null && <ConnectionPanel t={t} client={client} connection={opened} locale={locale} baseUrl={baseUrl} writeClipboard={writeClipboard} onClose={closed} now={now()} />}
    </main>
  );
}
