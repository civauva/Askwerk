// SPDX-License-Identifier: AGPL-3.0-only
import "../designer/designer.css";
import "./apiKeys.css";
import { ApiError, apiKeyPresets, apiKeyScopes, type ApiKeyItem, type ApiKeysClient, type NewApiKey, type OAuthClientsClient, type WebhooksClient } from "@askwerk/app-client";
import { KeyRowTools, type MakeConnection } from "./MakeConnection";
import { OAuthClientsPanel } from "./OAuthClientsPanel";
import { WebhooksPanel } from "./WebhooksPanel";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, DataGrid, dataGridTexts, Dialog as SharedDialog, type DataGridColumn, Tabs } from "@askwerk/ui";
import { useEffect, useId, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { apiKeysMessages, type ApiKeysMessageKey } from "./messages";
import { PageBar } from "../shell/PageBar";

type T = (key: ApiKeysMessageKey, values?: Record<string, string | number>) => string;

type ApiKeysPageProps = {
  client: ApiKeysClient;
  /** Webhooks (AWK-32): the Webhooks tab shows when given. */
  webhooks?: WebhooksClient;
  /** OAuth clients (AWK-32): the OAuth clients tab shows when given. */
  oauthClients?: OAuthClientsClient;
  locale: Locale;
  writeClipboard?: (text: string) => Promise<void>;
  timeZone?: string;
  now?: () => Date;
  /** "Make a connection of it" (W36); shown when given. */
  makeConnection?: MakeConnection;
};

type Preset = "readResults" | "fullAccess";

const same = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((scope) => b.includes(scope));

/**
 * Organisation → API (AWK-25, W14 C and D), one screen: the organisation's keys for the Public API on the shared
 * grid, creating a key in two steps (the secret is shown once), and revoking one.
 */
export function ApiKeysPage({ client, webhooks, oauthClients, locale, makeConnection, writeClipboard = (text) => navigator.clipboard.writeText(text), timeZone, now = () => new Date() }: ApiKeysPageProps) {
  const t: T = useMemo(() => createTranslator(apiKeysMessages, locale), [locale]);
  const [keys, setKeys] = useState<ApiKeyItem[] | null>(null);
  const [state, setState] = useState<"loaded" | "failed" | "refused">("loaded");
  const [creating, setCreating] = useState(false);
  const [revoking, setRevoking] = useState<ApiKeyItem | null>(null);
  const [tab, setTab] = useState<"keys" | "oauth" | "webhooks">("keys");
  const day = new Intl.DateTimeFormat(locale, { dateStyle: "medium", ...(timeZone === undefined ? {} : { timeZone }) });

  const reload = () =>
    client.list().then(
      (items) => {
        setKeys(items);
        setState("loaded");
      },
      (error: unknown) => setState(error instanceof ApiError && error.status === 403 ? "refused" : "failed"),
    );

  // Loads once per client; reload is recreated on every render.
  useEffect(() => {
    void reload();
  }, [client]);

  const access = (key: ApiKeyItem) => (same(key.scopes, apiKeyPresets.readResults) ? t("readResults") : same(key.scopes, apiKeyPresets.fullAccess) ? t("fullAccess") : t("custom"));
  const columns: DataGridColumn<ApiKeyItem>[] = [
    { key: "name", header: t("name"), phone: "title", cell: (key) => key.name },
    { key: "access", header: t("access"), cell: access },
    { key: "key", header: t("key"), cell: (key) => <code>{key.shown}</code> },
    { key: "created", header: t("created"), align: "end", cell: (key) => day.format(new Date(key.createdAt)) },
    { key: "lastUsed", header: t("lastUsed"), align: "end", cell: (key) => (key.lastUsedAt === null ? t("never") : day.format(new Date(key.lastUsedAt))) },
    {
      key: "expires",
      header: t("expires"),
      align: "end",
      cell: (key) => (key.expiresAt === null ? t("never") : new Date(key.expiresAt) <= now() ? t("expired") : day.format(new Date(key.expiresAt))),
    },
    {
      key: "revoke",
      header: t("webhookActions"),
      align: "end",
      cell: (key) => (
        <KeyRowTools t={t} kind="api-key" id={key.id} name={key.name} make={makeConnection} label={key.name} tools={[{ icon: "ban", label: t("revoke"), name: t("revokeOf", { name: key.name }), danger: true, onClick: () => setRevoking(key) }]} />
      ),
    },
  ];

  return (
    <main className="api-keys" lang={locale}>
      <PageBar
        path={[{ place: "administration" }, { place: "developers", to: "/developers" }]}
        title={t("title")}
        actions={
          state !== "refused" && tab === "keys" ? (
            <button type="button" className="designer-button api-keys__primary api-keys__create" onClick={() => setCreating(true)}>
              {t("createKey")}
            </button>
          ) : undefined
        }
      />
      <div className="api-keys__head">
        <div>
          <h1>{t("title")}</h1>
          <p className="api-keys__lead">{t("lead")}</p>
          <a className="api-keys__docs" href="/api/v1/openapi.json">
            {t("documentation")}
          </a>
        </div>
      </div>
      {(webhooks !== undefined || oauthClients !== undefined) && state !== "refused" && (
        <div className="api-keys__tabs">
          <Tabs
            label={t("sections")}
            tabs={[["keys", t("tabKeys")], ...(oauthClients === undefined ? [] : [["oauth", t("tabOAuth")] as const]), ...(webhooks === undefined ? [] : [["webhooks", t("tabWebhooks")] as const])]}
            value={tab}
            onChange={setTab}
          />
        </div>
      )}
      {state === "refused" ? (
        <p role="alert">{t("notAllowed")}</p>
      ) : tab === "oauth" && oauthClients !== undefined ? (
        <OAuthClientsPanel t={t} client={oauthClients} locale={locale} writeClipboard={writeClipboard} timeZone={timeZone} dialog={Dialog} makeConnection={makeConnection} />
      ) : tab === "webhooks" && webhooks !== undefined ? (
        <WebhooksPanel t={t} client={webhooks} locale={locale} writeClipboard={writeClipboard} timeZone={timeZone} dialog={Dialog} makeConnection={makeConnection} />
      ) : (
        <DataGrid
          label={t("keys")}
          columns={columns}
          rows={keys}
          rowKey={(key) => key.id}
          empty={<p>{t("empty")}</p>}
          error={state === "failed"}
          onRetry={() => void reload()}
          texts={dataGridTexts(locale === "de" ? "de" : "en")}
        />
      )}
      {creating && (
        <CreateKeyDialog
          t={t}
          client={client}
          writeClipboard={writeClipboard}
          onDone={() => {
            setCreating(false);
            void reload();
          }}
        />
      )}
      {revoking !== null && (
        <Dialog title={t("revokeTitle")} onClose={() => setRevoking(null)}>
          <p>{t("revokeAsk", { name: revoking.name })}</p>
          <div className="api-keys__buttons">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => setRevoking(null)}>
              {t("keep")}
            </button>
            <button
              type="button"
              className="designer-button api-keys__primary"
              onClick={() => {
                const key = revoking;
                setRevoking(null);
                client.revoke(key.id).then(() => void reload(), () => setState("failed"));
              }}
            >
              {t("revoke")}
            </button>
          </div>
        </Dialog>
      )}
    </main>
  );
}

/** A modal dialog with a title; Escape closes it. */
function Dialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  return (
    <SharedDialog title={title} onClose={onClose} className="api-keys__dialog">
      {children}
    </SharedDialog>
  );
}

/** Create key (W14 D): name, a preset, details under More; then the secret once, to copy. */
function CreateKeyDialog({ t, client, writeClipboard, onDone }: { t: T; client: ApiKeysClient; writeClipboard: (text: string) => Promise<void>; onDone: () => void }) {
  const id = useId();
  const nameField = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [preset, setPreset] = useState<Preset>("readResults");
  const [scopes, setScopes] = useState<readonly string[]>(apiKeyPresets.readResults);
  const [expiry, setExpiry] = useState<"30" | "90" | "365" | "never">("90");
  const [more, setMore] = useState(false);
  const [error, setError] = useState<ApiKeysMessageKey | null>(null);
  const [busy, setBusy] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => nameField.current?.focus(), []);

  const choosePreset = (next: Preset) => {
    setPreset(next);
    setScopes(apiKeyPresets[next]);
  };

  const toggle = (scope: string) =>
    setScopes((current) => apiKeyScopes.filter((each) => (each === scope ? !current.includes(scope) : current.includes(each))));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    const key: NewApiKey = { name: name.trim(), scopes: [...scopes], expiresInDays: expiry === "never" ? null : (Number(expiry) as 30 | 90 | 365) };

    try {
      setSecret((await client.create(key)).secret);
    } catch (caught) {
      const code = caught instanceof ApiError ? caught.problem.errors?.name?.[0] : undefined;
      setError(code === "name-required" ? "nameRequired" : code === "name-too-long" ? "nameTooLong" : "failed");
    } finally {
      setBusy(false);
    }
  }

  if (secret !== null) {
    return (
      <Dialog title={t("keyCreated")} onClose={onDone}>
        <p className="api-keys__strong">{t("copyNow")}</p>
        <div className="api-keys__secret">
          <code>{secret}</code>
          <button type="button" className="designer-button api-keys__primary" onClick={() => void writeClipboard(secret).then(() => setCopied(true), () => undefined)}>
            {copied ? t("copied") : t("copy")}
          </button>
        </div>
        <p className="api-keys__muted">{t("sendAs")}</p>
        <p className="api-keys__muted">{t("keepSafe")}</p>
        <div className="api-keys__buttons">
          <button type="button" className="designer-button" onClick={onDone}>
            {t("done")}
          </button>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog title={t("createKey")} onClose={onDone}>
      <form className="api-keys__form" onSubmit={(event) => void submit(event)} noValidate>
        <label className="api-keys__label" htmlFor={`${id}-name`}>
          {t("name")}
        </label>
        <input
          id={`${id}-name`}
          ref={nameField}
          className="aw-field"
          maxLength={100}
          value={name}
          aria-invalid={error === "nameRequired" || error === "nameTooLong"}
          aria-describedby={error !== null ? `${id}-error` : undefined}
          onChange={(event) => setName(event.target.value)}
        />
        <fieldset className="api-keys__fieldset">
          <legend className="api-keys__label">{t("access")}</legend>
          {(["readResults", "fullAccess"] as const).map((each) => (
            <label key={each} className="api-keys__radio">
              <input type="radio" name={`${id}-preset`} checked={preset === each} onChange={() => choosePreset(each)} />
              <span>
                {t(each)}
                <small>{t(each === "readResults" ? "readResultsHint" : "fullAccessHint")}</small>
              </span>
            </label>
          ))}
        </fieldset>
        <button type="button" className="results-link-button" aria-expanded={more} onClick={() => setMore((open) => !open)}>
          {t("more")}
          <span aria-hidden="true"> {more ? "▴" : "▾"}</span>
        </button>
        {more && (
          <>
            <fieldset className="api-keys__fieldset api-keys__scopes">
              <legend className="api-keys__label">{t("scopes")}</legend>
              {apiKeyScopes.map((scope) => (
                <label key={scope} className="api-keys__check">
                  <input type="checkbox" checked={scopes.includes(scope)} onChange={() => toggle(scope)} />
                  <code>{scope}</code>
                </label>
              ))}
            </fieldset>
            <span id={`${id}-expiry`} className="api-keys__label">
              {t("expires")}
            </span>
            <ComboBox
              labelledBy={`${id}-expiry`}
              options={[
                { value: "30", label: t("in30") },
                { value: "90", label: t("in90") },
                { value: "365", label: t("in365") },
                { value: "never", label: t("never") },
              ]}
              value={expiry}
              onChange={(value) => setExpiry(value as typeof expiry)}
              texts={{ choose: t("expires") }}
            />
          </>
        )}
        {error !== null && (
          <span id={`${id}-error`} className="aw-field-error">
            {t(error)}
          </span>
        )}
        <div className="api-keys__buttons">
          <button type="button" className="designer-button designer-button--quiet" onClick={onDone}>
            {t("cancel")}
          </button>
          <button type="submit" className="designer-button api-keys__primary" disabled={busy || scopes.length === 0}>
            {t("create")}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
