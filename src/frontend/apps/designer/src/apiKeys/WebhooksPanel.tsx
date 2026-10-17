// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, webhookEvents, type WebhookDeliveryItem, type WebhookItem, type WebhooksClient } from "@askwerk/app-client";
import { DataGrid, dataGridTexts, type DataGridColumn } from "@askwerk/ui";
import { useEffect, useId, useState, type ReactNode } from "react";
import { MakeConnectionButton, type MakeConnection } from "./MakeConnection";
import type { ApiKeysMessageKey } from "./messages";

type T = (key: ApiKeysMessageKey, values?: Record<string, string | number>) => string;

/** Organisation → API → Webhooks (AWK-32, C32): the webhooks with their last delivery, adding one, the deliveries log. */
export function WebhooksPanel({
  t,
  client,
  locale,
  writeClipboard,
  timeZone,
  dialog: Dialog,
  makeConnection,
}: {
  t: T;
  client: WebhooksClient;
  locale: string;
  writeClipboard: (text: string) => Promise<void>;
  timeZone: string | undefined;
  makeConnection?: MakeConnection | undefined;
  dialog: (props: { title: string; onClose: () => void; children: ReactNode }) => ReactNode;
}) {
  const [items, setItems] = useState<WebhookItem[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [adding, setAdding] = useState(false);
  const [deleting, setDeleting] = useState<WebhookItem | null>(null);
  const [log, setLog] = useState<{ webhook: WebhookItem; deliveries: WebhookDeliveryItem[] } | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const logId = useId();
  const time = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", hourCycle: "h23", ...(timeZone === undefined ? {} : { timeZone }) });

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

  const last = (webhook: WebhookItem) => {
    if (webhook.paused) {
      return t("pausedFailing");
    }

    if (webhook.lastDelivery === null) {
      return webhook.active ? t("noDelivery") : t("off");
    }

    const at = time.format(new Date(webhook.lastDelivery.at));
    return webhook.lastDelivery.status === "delivered" ? t("deliveredAt", { time: at }) : webhook.lastDelivery.status === "failed" ? t("failedAt", { time: at }) : t("pendingAt", { time: at });
  };

  const openLog = (webhook: WebhookItem) => client.deliveries(webhook.id).then((deliveries) => setLog({ webhook, deliveries }), () => setFailed(true));

  const columns: DataGridColumn<WebhookItem>[] = [
    { key: "url", header: t("webhookUrlColumn"), phone: "title", cell: (webhook) => <code>{webhook.url}</code> },
    { key: "events", header: t("webhookEventsColumn"), cell: (webhook) => webhook.events.join(", ") },
    { key: "last", header: t("webhookLastColumn"), cell: (webhook) => <span className="api-keys__pill">{last(webhook)}</span> },
    {
      key: "actions",
      header: t("webhookActions"),
      align: "end",
      cell: (webhook) => (
        <span className="api-keys__actions">
          {webhook.paused || !webhook.active ? (
            <button type="button" className="api-keys__revoke" aria-label={t("resumeOf", { url: webhook.url })} onClick={() => void client.change(webhook.id, { active: true }).then(reload)}>
              {t("resume")}
            </button>
          ) : (
            <button
              type="button"
              className="api-keys__revoke"
              aria-label={t("testOf", { url: webhook.url })}
              onClick={() => void client.ping(webhook.id).then(() => setNotice(t("testSent")), () => setFailed(true))}
            >
              {t("testShort")}
            </button>
          )}
          {makeConnection !== undefined && <MakeConnectionButton t={t} kind="webhook" id={webhook.id} name={webhook.description ?? webhook.url} make={makeConnection} />}
          <button type="button" className="api-keys__revoke" aria-label={t("deliveriesOf", { url: webhook.url })} onClick={() => void openLog(webhook)}>
            {t("deliveriesShort")}
          </button>
          <button type="button" className="api-keys__revoke" aria-label={t("deleteOf", { url: webhook.url })} onClick={() => setDeleting(webhook)}>
            {t("deleteShort")}
          </button>
        </span>
      ),
    },
  ];

  return (
    <section className="api-keys__webhooks">
      <div className="api-keys__head">
        <p className="api-keys__lead">{t("webhooksLead")}</p>
        <button type="button" className="designer-button api-keys__primary api-keys__create" onClick={() => setAdding(true)}>
          {t("addWebhook")}
        </button>
      </div>
      {notice !== null && (
        <p role="status" className="api-keys__lead">
          {notice}
        </p>
      )}
      <DataGrid
        label={t("webhooks")}
        columns={columns}
        rows={items}
        rowKey={(webhook) => webhook.id}
        empty={<p>{t("webhooksEmpty")}</p>}
        error={failed}
        onRetry={() => void reload()}
        texts={dataGridTexts(locale === "de" ? "de" : "en")}
      />
      {log !== null && (
        <section className="api-keys__log" aria-labelledby={logId}>
          <h2 id={logId}>{t("deliveriesTitle", { url: log.webhook.url })}</h2>
          {log.deliveries.length === 0 ? (
            <p>{t("noDeliveries")}</p>
          ) : (
            <ul>
              {log.deliveries.map((delivery) => {
                const at = time.format(new Date(delivery.createdAt));
                const status = delivery.responseStatus === null ? delivery.status : String(delivery.responseStatus);

                return (
                  <li key={delivery.id}>
                    <code>{delivery.event}</code>
                    <span>{delivery.attempts === 1 ? t("deliveryLineOne", { time: at, status }) : t("deliveryLine", { time: at, status, attempts: delivery.attempts })}</span>
                    <button
                      type="button"
                      className="api-keys__revoke"
                      aria-label={t("replayOf", { event: delivery.event, time: at })}
                      onClick={() => void client.replay(log.webhook.id, delivery.id).then(() => openLog(log.webhook), () => setFailed(true))}
                    >
                      {t("replay")}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      )}
      {adding && (
        <AddWebhook
          t={t}
          client={client}
          writeClipboard={writeClipboard}
          dialog={Dialog}
          onDone={() => {
            setAdding(false);
            void reload();
          }}
        />
      )}
      {deleting !== null && (
        <Dialog title={t("deleteWebhookTitle")} onClose={() => setDeleting(null)}>
          <p>{t("deleteWebhookAsk", { url: deleting.url })}</p>
          <div className="api-keys__buttons">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => setDeleting(null)}>
              {t("keep")}
            </button>
            <button
              type="button"
              className="designer-button api-keys__primary"
              onClick={() => {
                const webhook = deleting;
                setDeleting(null);
                setLog((current) => (current?.webhook.id === webhook.id ? null : current));
                void client.remove(webhook.id).then(reload, () => setFailed(true));
              }}
            >
              {t("delete")}
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}

/** Add webhook: URL, events (all preselected), description; then the secret once, to copy. */
function AddWebhook({
  t,
  client,
  writeClipboard,
  dialog: Dialog,
  onDone,
}: {
  t: T;
  client: WebhooksClient;
  writeClipboard: (text: string) => Promise<void>;
  dialog: (props: { title: string; onClose: () => void; children: ReactNode }) => ReactNode;
  onDone: () => void;
}) {
  const id = useId();
  const [url, setUrl] = useState("");
  const [events, setEvents] = useState<string[]>([...webhookEvents]);
  const [description, setDescription] = useState("");
  const [error, setError] = useState<ApiKeysMessageKey | null>(null);
  const [busy, setBusy] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  return (
    <Dialog title={t("addWebhookTitle")} onClose={onDone}>
      {secret === null ? (
        <form
          className="api-keys__form"
          onSubmit={(event) => {
            event.preventDefault();
            setBusy(true);
            setError(null);
            client.create({ url: url.trim(), events, description: description.trim() }).then(
              (created) => setSecret(created.secret),
              (failure: unknown) => {
                setBusy(false);
                const code = failure instanceof ApiError ? failure.problem.code : undefined;
                setError(code === "url-invalid" ? "urlInvalid" : code === "webhooks-too-many" ? "webhooksTooMany" : "failed");
              },
            );
          }}
        >
          <label className="designer-label" htmlFor={`${id}-url`}>
            {t("webhookUrl")}
          </label>
          <input id={`${id}-url`} className="designer-input" type="url" autoFocus value={url} aria-describedby={`${id}-url-hint`} onChange={(event) => setUrl(event.target.value)} />
          <span id={`${id}-url-hint`} className="api-keys__hint">
            {t("webhookUrlHint")}
          </span>
          <fieldset className="api-keys__scopes">
            <legend className="designer-label">{t("webhookEvents")}</legend>
            {webhookEvents.map((each) => (
              <label key={each}>
                <input type="checkbox" checked={events.includes(each)} onChange={(event) => setEvents(event.target.checked ? [...events, each] : events.filter((other) => other !== each))} />
                <code>{each}</code>
              </label>
            ))}
          </fieldset>
          <label className="designer-label" htmlFor={`${id}-description`}>
            {t("webhookDescription")}
          </label>
          <input id={`${id}-description`} className="designer-input" value={description} maxLength={200} onChange={(event) => setDescription(event.target.value)} />
          {error !== null && (
            <p role="alert" className="api-keys__error">
              {t(error)}
            </p>
          )}
          <div className="api-keys__buttons">
            <button type="button" className="designer-button designer-button--quiet" onClick={onDone}>
              {t("cancel")}
            </button>
            <button type="submit" className="designer-button api-keys__primary" disabled={busy || url.trim() === "" || events.length === 0}>
              {t("add")}
            </button>
          </div>
        </form>
      ) : (
        <>
          <p>{t("webhookSecret")}</p>
          <p className="api-keys__secret">
            <code>{secret}</code>
            <button type="button" className="designer-button" onClick={() => void writeClipboard(secret).then(() => setCopied(true))}>
              {copied ? t("copied") : t("copy")}
            </button>
          </p>
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
