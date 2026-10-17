// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type ConnectionCredentials, type ConnectionItem, type ConnectionsClient } from "@askwerk/app-client";
import type { Locale } from "@askwerk/schema";
import { ConfirmDialog, Dialog } from "@askwerk/ui";
import { useId, useState } from "react";
import { CodeTabs, Credentials, eventOptions, goalKeys, Health, permissionKeys, setupOf, type T } from "./parts";

type ConnectionPanelProps = {
  t: T;
  client: ConnectionsClient;
  connection: ConnectionItem;
  locale: Locale;
  baseUrl: string;
  writeClipboard: (text: string) => Promise<void>;
  onClose: (changed: boolean) => void;
  now?: Date;
};

/**
 * One connection (W36): whether it works and what it does in both directions; its name, address and matching to
 * change; a test event, new secrets (shown once), pause and remove.
 */
export function ConnectionPanel({ t, client, connection: first, locale, baseUrl, writeClipboard, onClose, now = new Date() }: ConnectionPanelProps) {
  const id = useId();
  const [connection, setConnection] = useState(first);
  const [name, setName] = useState(first.name);
  const [url, setUrl] = useState(first.webhook?.url ?? "");
  const [matchBy, setMatchBy] = useState(first.matchBy);
  const [changed, setChanged] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<"secrets" | "remove" | null>(null);
  const [credentials, setCredentials] = useState<ConnectionCredentials | null>(null);
  const dates = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" });
  const { webhook, calls } = connection;
  const people = connection.goals.includes("people");

  const update = (next: ConnectionItem, text?: string) => {
    setConnection(next);
    setChanged(true);
    setNotice(text ?? null);
  };
  const failed = () => setNotice(t("failed"));

  const save = () => {
    setError(null);
    client.change(connection.id, { name: name.trim(), ...(webhook !== null && url.trim() !== webhook.url ? { webhookUrl: url.trim() } : {}), ...(people ? { matchBy } : {}) }).then(
      (next) => update(next, t("saved")),
      (caught: unknown) => (caught instanceof ApiError && caught.problem.errors?.webhookUrl !== undefined ? setError(t("urlInvalid")) : failed()),
    );
  };

  return (
    <>
      <Dialog title={connection.name} kicker={t("connection")} onClose={() => onClose(changed)} wide className="connection-panel">
        <div className="connection-panel__status">
          <Health t={t} locale={locale} connection={connection} now={now} />
          <span className="connection-chips">
            {connection.goals.map((goal) => (
              <span key={goal} className="connection-chip">
                {goalKeys[goal].mark} {t(goalKeys[goal].chip)}
              </span>
            ))}
          </span>
        </div>

        <div className="connect-form">
          <label htmlFor={`${id}-name`}>{t("name")}</label>
          <input id={`${id}-name`} className="designer-input" value={name} onChange={(event) => setName(event.target.value)} />
          {webhook !== null && (
            <>
              <label htmlFor={`${id}-url`}>{t("answersTo")}</label>
              <span>
                <input id={`${id}-url`} className="designer-input connection-mono" type="url" value={url} aria-invalid={error !== null} onChange={(event) => setUrl(event.target.value)} />
                {error !== null && <span className="designer-error">{error}</span>}
                <small className="connection-muted">
                  {eventOptions.filter((option) => option.events.some((event) => webhook.events.includes(event))).map((option) => t(option.label)).join(" · ")} · {webhook.surveys.length === 0 ? t("allSurveys") : webhook.surveys.map((survey) => survey.title).join(", ")}
                </small>
              </span>
            </>
          )}
          {calls !== null && (
            <>
              <span>{t("theyCall")}</span>
              <span>
                {calls.signIn === "oauth" ? t("oauthClient") : t("apiKey")} · <code>{calls.shown}</code> · {calls.permissions.map((permission) => t(permissionKeys[permission].short)).join(", ")}
                <small className="connection-muted">
                  {t("lastActivityLabel")}: {calls.lastCallAt === null ? t("never") : dates.format(new Date(calls.lastCallAt))}
                </small>
              </span>
            </>
          )}
          {people && (
            <>
              <span id={`${id}-match`}>{t("matchBy")}</span>
              <span role="radiogroup" aria-labelledby={`${id}-match`} className="connect-checks">
                <label>
                  <input type="radio" name={`${id}-match`} checked={matchBy === "external-id"} onChange={() => setMatchBy("external-id")} />
                  {t("matchExternal")}
                </label>
                <label>
                  <input type="radio" name={`${id}-match`} checked={matchBy === "email"} onChange={() => setMatchBy("email")} />
                  {t("matchEmail")}
                </label>
              </span>
            </>
          )}
          <span>{t("created")}</span>
          <span>{dates.format(new Date(connection.createdAt))}</span>
        </div>

        {credentials !== null && <Credentials t={t} credentials={credentials} writeClipboard={writeClipboard} />}
        {credentials !== null && <CodeTabs t={t} setup={setupOf(connection, credentials.clientId, baseUrl)} />}

        {notice !== null && (
          <p role="status" className="connection-notice">
            {notice}
          </p>
        )}
        <div className="connect-actions">
          {webhook !== null && (
            <button type="button" className="designer-button" disabled={connection.pausedAt !== null} onClick={() => void client.test(connection.id).then(() => setNotice(t("testWaiting", { host: new URL(webhook.url).host })), failed)}>
              {t("sendTest")}
            </button>
          )}
          {(webhook !== null || calls !== null) && (
            <button type="button" className="designer-button" onClick={() => setConfirming("secrets")}>
              {t("newSecrets")}
            </button>
          )}
          <button type="button" className="designer-button" onClick={() => void client.pause(connection.id, connection.pausedAt === null).then((next) => update(next), failed)}>
            {connection.pausedAt === null ? t("pause") : t("resume")}
          </button>
          <button type="button" className="designer-button connection-remove" onClick={() => setConfirming("remove")}>
            {t("remove")}
          </button>
          <span className="connection-grow" />
          <button type="button" className="designer-button designer-button--primary" onClick={save}>
            {t("save")}
          </button>
        </div>
      </Dialog>
      {confirming === "secrets" && (
        <ConfirmDialog
          title={t("newSecretsTitle")}
          text={t("newSecretsText")}
          confirm={t("newSecretsConfirm")}
          cancel={t("cancel")}
          onCancel={() => setConfirming(null)}
          onConfirm={() =>
            void client.newSecrets(connection.id).then(
              (fresh) => {
                setConfirming(null);
                setCredentials(fresh);
                setChanged(true);
              },
              () => {
                setConfirming(null);
                failed();
              },
            )
          }
        />
      )}
      {confirming === "remove" && (
        <ConfirmDialog
          title={t("removeTitle", { name: connection.name })}
          text={t("removeText")}
          confirm={t("remove")}
          cancel={t("cancel")}
          onCancel={() => setConfirming(null)}
          onConfirm={() =>
            void client.remove(connection.id).then(
              () => onClose(true),
              () => {
                setConfirming(null);
                failed();
              },
            )
          }
        />
      )}
    </>
  );
}
