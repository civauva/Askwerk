// SPDX-License-Identifier: AGPL-3.0-only
import { apiKeyPresets, type OAuthClientItem, type OAuthClientsClient } from "@askwerk/app-client";
import { DataGrid, dataGridTexts, type DataGridColumn } from "@askwerk/ui";
import { useEffect, useId, useState, type ReactNode } from "react";
import type { ApiKeysMessageKey } from "./messages";

type T = (key: ApiKeysMessageKey, values?: Record<string, string | number>) => string;
type Dialog = (props: { title: string; onClose: () => void; children: ReactNode }) => ReactNode;

const same = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((scope) => b.includes(scope));

/** Organisation → API → OAuth clients (AWK-32, C32 B): the clients, creating one with its secret shown once, revoking. */
export function OAuthClientsPanel({ t, client, locale, writeClipboard, timeZone, dialog: Dialog }: { t: T; client: OAuthClientsClient; locale: string; writeClipboard: (text: string) => Promise<void>; timeZone: string | undefined; dialog: Dialog }) {
  const [items, setItems] = useState<OAuthClientItem[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [creating, setCreating] = useState(false);
  const [revoking, setRevoking] = useState<OAuthClientItem | null>(null);
  const day = new Intl.DateTimeFormat(locale, { dateStyle: "medium", ...(timeZone === undefined ? {} : { timeZone }) });

  const reload = () =>
    client.list().then(
      (list) => {
        setItems(list);
        setFailed(false);
      },
      () => setFailed(true),
    );

  // Loads once per client.
  useEffect(() => {
    void reload();
  }, [client]);

  const access = (item: OAuthClientItem) => (same(item.scopes, apiKeyPresets.readResults) ? t("readResults") : same(item.scopes, apiKeyPresets.fullAccess) ? t("fullAccess") : t("custom"));
  const columns: DataGridColumn<OAuthClientItem>[] = [
    { key: "name", header: t("name"), phone: "title", cell: (item) => item.name },
    { key: "clientId", header: t("clientId"), cell: (item) => <code>{item.clientId}</code> },
    { key: "access", header: t("access"), cell: access },
    { key: "lastUsed", header: t("lastUsed"), align: "end", cell: (item) => (item.lastUsedAt === null ? t("never") : day.format(new Date(item.lastUsedAt))) },
    { key: "expires", header: t("expires"), align: "end", cell: (item) => (item.expiresAt === null ? t("never") : day.format(new Date(item.expiresAt))) },
    {
      key: "revoke",
      header: t("revoke"),
      align: "end",
      cell: (item) => (
        <button type="button" className="api-keys__revoke" aria-label={t("revokeOf", { name: item.name })} onClick={() => setRevoking(item)}>
          {t("revoke")}
        </button>
      ),
    },
  ];

  return (
    <section>
      <div className="api-keys__head">
        <p className="api-keys__lead">{t("oauthLead")}</p>
        <button type="button" className="designer-button api-keys__primary api-keys__create" onClick={() => setCreating(true)}>
          {t("createClient")}
        </button>
      </div>
      <DataGrid
        label={t("oauthClients")}
        columns={columns}
        rows={items}
        rowKey={(item) => item.id}
        empty={<p>{t("oauthEmpty")}</p>}
        error={failed}
        onRetry={() => void reload()}
        texts={dataGridTexts(locale === "de" ? "de" : "en")}
      />
      {creating && (
        <CreateClient
          t={t}
          client={client}
          writeClipboard={writeClipboard}
          dialog={Dialog}
          onDone={() => {
            setCreating(false);
            void reload();
          }}
        />
      )}
      {revoking !== null && (
        <Dialog title={t("revokeClientTitle")} onClose={() => setRevoking(null)}>
          <p>{t("revokeClientAsk", { name: revoking.name })}</p>
          <div className="api-keys__buttons">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => setRevoking(null)}>
              {t("keep")}
            </button>
            <button
              type="button"
              className="designer-button api-keys__primary"
              onClick={() => {
                const item = revoking;
                setRevoking(null);
                void client.revoke(item.id).then(reload, () => setFailed(true));
              }}
            >
              {t("revoke")}
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}

function CreateClient({ t, client, writeClipboard, dialog: Dialog, onDone }: { t: T; client: OAuthClientsClient; writeClipboard: (text: string) => Promise<void>; dialog: Dialog; onDone: () => void }) {
  const id = useId();
  const [name, setName] = useState("");
  const [preset, setPreset] = useState<"readResults" | "fullAccess">("readResults");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiKeysMessageKey | null>(null);
  const [created, setCreated] = useState<{ clientId: string; secret: string } | null>(null);
  const [copied, setCopied] = useState(false);

  return (
    <Dialog title={t("createClientTitle")} onClose={onDone}>
      {created === null ? (
        <form
          className="api-keys__form"
          onSubmit={(event) => {
            event.preventDefault();

            if (name.trim() === "") {
              setError("nameRequired");
              return;
            }

            setBusy(true);
            client.create({ name: name.trim(), scopes: apiKeyPresets[preset], expiresInDays: 365 }).then(
              (answer) => setCreated({ clientId: answer.client.clientId, secret: answer.clientSecret }),
              () => {
                setBusy(false);
                setError("failed");
              },
            );
          }}
        >
          <label className="designer-label" htmlFor={`${id}-name`}>
            {t("name")}
          </label>
          <input id={`${id}-name`} className="designer-input" autoFocus maxLength={100} value={name} onChange={(event) => setName(event.target.value)} />
          <fieldset className="api-keys__scopes">
            <legend className="designer-label">{t("access")}</legend>
            {(["readResults", "fullAccess"] as const).map((each) => (
              <label key={each}>
                <input type="radio" name={`${id}-preset`} checked={preset === each} onChange={() => setPreset(each)} />
                <b>{t(each)}</b> <span className="api-keys__hint">{t(each === "readResults" ? "readResultsHint" : "fullAccessHint")}</span>
              </label>
            ))}
          </fieldset>
          <p className="api-keys__hint">{t("clientExpiry")}</p>
          {error !== null && (
            <p role="alert" className="api-keys__error">
              {t(error)}
            </p>
          )}
          <div className="api-keys__buttons">
            <button type="button" className="designer-button designer-button--quiet" onClick={onDone}>
              {t("cancel")}
            </button>
            <button type="submit" className="designer-button api-keys__primary" disabled={busy}>
              {t("create")}
            </button>
          </div>
        </form>
      ) : (
        <>
          <p>{t("clientCreated")}</p>
          <p className="designer-label">{t("clientId")}</p>
          <p className="api-keys__secret">
            <code>{created.clientId}</code>
          </p>
          <p className="designer-label">{t("clientSecret")}</p>
          <p className="api-keys__secret">
            <code>{created.secret}</code>
            <button type="button" className="designer-button" aria-label={t("copySecret")} onClick={() => void writeClipboard(created.secret).then(() => setCopied(true))}>
              {copied ? t("copied") : t("copy")}
            </button>
          </p>
          <p className="api-keys__hint">{t("tokenHow")}</p>
          <div className="api-keys__buttons">
            <button type="button" className="designer-button api-keys__primary" onClick={onDone}>
              {t("done")}
            </button>
          </div>
        </>
      )}
    </Dialog>
  );
}
