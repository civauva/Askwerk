// SPDX-License-Identifier: AGPL-3.0-only
import type { ConnectionCredentials, ConnectionGoal, ConnectionItem, ConnectionPermission } from "@askwerk/app-client";
import type { Locale, Translate } from "@askwerk/schema";
import { useId, useState } from "react";
import type { ConnectionsMessageKey } from "./messages";
import { sampleLanguages, sampleNames, sampleOf, type SampleLanguage, type SampleSetup } from "./samples";

export type T = Translate<ConnectionsMessageKey>;

export const goalKeys: Record<ConnectionGoal, { chip: ConnectionsMessageKey; mark: string; title: ConnectionsMessageKey; text: ConnectionsMessageKey }> = {
  answers: { chip: "goalAnswers", mark: "↓", title: "answersTitle", text: "answersText" },
  people: { chip: "goalPeople", mark: "↑", title: "peopleTitle", text: "peopleText" },
  "sign-in": { chip: "goalSignIn", mark: "⇄", title: "signInTitle", text: "signInText" },
  embed: { chip: "goalEmbed", mark: "▣", title: "embedTitle", text: "embedText" },
};

export const permissionKeys: Record<ConnectionPermission, { short: ConnectionsMessageKey; long: ConnectionsMessageKey; scopes: string }> = {
  people: { short: "permPeople", long: "mayPeople", scopes: "participants:read · participants:write" },
  results: { short: "permResults", long: "mayResults", scopes: "surveys:read · responses:read" },
  surveys: { short: "permSurveys", long: "maySurveys", scopes: "surveys:write" },
};

export const eventKeys: [string, ConnectionsMessageKey][] = [
  ["response.completed", "eventResponse"],
  ["survey.published", "eventPublished"],
  ["survey.closed", "eventClosed"],
  ["export.completed", "eventExport"],
];

const healthKeys: Record<ConnectionItem["health"], ConnectionsMessageKey> = {
  works: "healthWorks",
  waiting: "healthWaiting",
  warning: "healthWarning",
  failing: "healthFailing",
  paused: "healthPaused",
};

/** "4 minutes ago", "yesterday", in the reader's language. */
export function ago(locale: Locale, at: string, now: Date): string {
  const minutes = Math.round((Date.parse(at) - now.getTime()) / 60_000);
  const format = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });

  return Math.abs(minutes) < 60 ? format.format(minutes, "minute") : Math.abs(minutes) < 1440 ? format.format(Math.round(minutes / 60), "hour") : format.format(Math.round(minutes / 1440), "day");
}

/** The host and path of a URL, without https://. */
export function shortUrl(url: string): string {
  return url.replace(/^https?:\/\//, "").replace(/\/$/, "");
}

/** Whether it works, and the one line that says why or how much. */
export function Health({ t, locale, connection, now }: { t: T; locale: Locale; connection: ConnectionItem; now: Date }) {
  const day = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short" });
  const { health, reason, webhook, calls } = connection;
  const detail =
    health === "paused"
      ? t("pausedDetail", { date: day.format(new Date(connection.pausedAt ?? connection.createdAt)), name: connection.pausedBy ?? "—" })
      : reason === "credentials-ending"
        ? t("reasonEnding", { date: day.format(new Date(calls?.expiresAt ?? connection.createdAt)) })
        : reason === "credentials-ended"
          ? t("reasonEnded")
          : reason === "deliveries-failing"
            ? t("reasonFailing", { date: day.format(new Date(webhook?.failingSince ?? connection.createdAt)) })
            : reason === "webhook-paused"
              ? t("reasonWebhookPaused")
              : reason === "deliveries-failed"
                ? t("reasonFailed")
                : health === "waiting"
                  ? t("waitingDetail")
                  : [webhook === null ? null : t("deliveredWeek", { count: webhook.deliveredThisWeek }), connection.lastActivityAt === null ? null : t("lastActivity", { when: ago(locale, connection.lastActivityAt, now) })].filter(Boolean).join(" · ");

  return (
    <span className="connection-health" data-health={health}>
      <b>{t(healthKeys[health])}</b>
      {detail !== "" && <span>{detail}</span>}
    </span>
  );
}

/** The secrets, once: each masked until shown, each to copy. */
export function Credentials({ t, credentials, writeClipboard }: { t: T; credentials: ConnectionCredentials; writeClipboard: (text: string) => Promise<void> }) {
  const rows: [ConnectionsMessageKey, string, boolean][] = [];
  credentials.clientId !== null && rows.push(["clientId", credentials.clientId, false]);
  credentials.clientSecret !== null && rows.push(["clientSecret", credentials.clientSecret, true]);
  credentials.apiKey !== null && rows.push(["apiKeyLabel", credentials.apiKey, true]);
  credentials.webhookSecret !== null && rows.push(["webhookSecret", credentials.webhookSecret, true]);

  if (rows.length === 0) {
    return null;
  }

  return (
    <section className="connection-credentials" aria-label={t("credentials")}>
      <h3>{t("credentials")}</h3>
      <dl>
        {rows.map(([label, value, secret]) => (
          <Secret key={label} t={t} label={t(label)} value={value} secret={secret} writeClipboard={writeClipboard} />
        ))}
      </dl>
      <p className="connection-muted">{t("credentialsHint")}</p>
    </section>
  );
}

function Secret({ t, label, value, secret, writeClipboard }: { t: T; label: string; value: string; secret: boolean; writeClipboard: (text: string) => Promise<void> }) {
  const [shown, setShown] = useState(!secret);
  const [copied, setCopied] = useState(false);

  return (
    <div className="connection-secret">
      <dt>{label}</dt>
      <dd>
        <code aria-label={label}>{shown ? value : "•".repeat(28)}</code>
        {secret && (
          <button type="button" className="designer-button" onClick={() => setShown(!shown)}>
            {shown ? t("hide") : t("show")}
          </button>
        )}
        <button type="button" className="designer-button" aria-label={`${t("copy")}: ${label}`} onClick={() => void writeClipboard(value).then(() => setCopied(true), () => undefined)}>
          {copied ? t("copied") : t("copy")}
        </button>
      </dd>
    </div>
  );
}

/** The code the system runs, in five languages. */
export function CodeTabs({ t, setup }: { t: T; setup: SampleSetup }) {
  const id = useId();
  const [language, setLanguage] = useState<SampleLanguage>("csharp");

  return (
    <section className="connection-code" aria-label={t("code")}>
      <div role="tablist" aria-label={t("code")} className="connection-code__tabs">
        {sampleLanguages.map((each) => (
          <button key={each} type="button" role="tab" id={`${id}-${each}`} aria-selected={each === language} aria-controls={`${id}-panel`} onClick={() => setLanguage(each)}>
            {sampleNames[each]}
          </button>
        ))}
      </div>
      <pre id={`${id}-panel`} role="tabpanel" aria-labelledby={`${id}-${language}`} tabIndex={0} className="connection-code__body">
        {sampleOf(language, setup)}
      </pre>
    </section>
  );
}

/** The sample setup of a connection. */
export function setupOf(connection: ConnectionItem, clientId: string | null, baseUrl: string): SampleSetup {
  return {
    baseUrl,
    signIn: connection.calls?.signIn ?? null,
    clientId: clientId ?? (connection.calls?.signIn === "oauth" ? connection.calls.shown : null),
    people: connection.calls?.permissions.includes("people") ?? false,
    results: connection.calls?.permissions.includes("results") ?? false,
    webhook: connection.webhook !== null,
  };
}
