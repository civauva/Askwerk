// SPDX-License-Identifier: AGPL-3.0-only
import type { ConnectionCredentials, ConnectionGoal, ConnectionItem, ConnectionPermission } from "@askwerk/app-client";
import type { Locale, Translate } from "@askwerk/schema";
import { useId, useState } from "react";
import type { ConnectionsMessageKey } from "./messages";
import { sampleLanguages, sampleNames, sampleOf, type SampleLanguage, type SampleSetup } from "./samples";

export type T = Translate<ConnectionsMessageKey>;

export const goalKeys: Record<ConnectionGoal, { chip: ConnectionsMessageKey; mark: string; title: ConnectionsMessageKey; text: ConnectionsMessageKey; uses: readonly string[] }> = {
  answers: { chip: "goalAnswers", mark: "↓", title: "answersTitle", text: "answersText", uses: ["webhook response.completed", "read results"] },
  people: { chip: "goalPeople", mark: "↑", title: "peopleTitle", text: "peopleText", uses: ["participants", "External ID"] },
  "sign-in": { chip: "goalSignIn", mark: "⇄", title: "signInTitle", text: "signInText", uses: ["OpenID Connect", "LDAP"] },
  embed: { chip: "goalEmbed", mark: "▣", title: "embedTitle", text: "embedText", uses: ["embed", "signed hand-over"] },
};

export const permissionKeys: Record<ConnectionPermission, { short: ConnectionsMessageKey; long: ConnectionsMessageKey; scopes: string }> = {
  people: { short: "permPeople", long: "mayPeople", scopes: "participants:read · participants:write" },
  results: { short: "permResults", long: "mayResults", scopes: "surveys:read · responses:read" },
  surveys: { short: "permSurveys", long: "maySurveys", scopes: "surveys:write" },
};

/** The webhook's choices: one line each, with the events it subscribes. */
export const eventOptions: { events: readonly string[]; label: ConnectionsMessageKey; detail: ConnectionsMessageKey | null }[] = [
  { events: ["response.completed"], label: "eventResponse", detail: "eventResponseDetail" },
  { events: ["survey.published", "survey.closed"], label: "eventSurvey", detail: null },
  { events: ["export.completed"], label: "eventExport", detail: null },
];

/** The tile of a connection: the kind of system named in it (CRM, HR…), else what it does; its colour from its goals. */
export function tileOf(connection: Pick<ConnectionItem, "name" | "goals">): { label: string; colour: string } {
  const { name, goals } = connection;
  const named = /\b(CRM|ERP|HR|HRIS|BI|LMS|CMS|ITSM)\b/i.exec(name)?.[1]?.toUpperCase();
  const label =
    named ??
    (goals.includes("embed")
      ? "WEB"
      : goals.length === 1 && goals[0] === "sign-in"
        ? "SSO"
        : name
            .split(/\s+/)
            .filter(Boolean)
            .slice(0, 2)
            .map((word) => word[0]!.toUpperCase())
            .join("") || "?");
  const colour = goals.includes("embed") ? "#b8860b" : goals.includes("answers") && goals.includes("people") ? "#1f5f8b" : goals.includes("answers") ? "#2f6f4f" : "#7c5cff";

  return { label, colour };
}

/** "4 min ago", "2 h ago", "3 days ago". */
export function ago(t: T, at: string, now: Date): string {
  const minutes = Math.max(0, Math.round((now.getTime() - Date.parse(at)) / 60_000));

  return minutes < 1 ? t("justNow") : minutes < 60 ? t("agoMinutes", { n: minutes }) : minutes < 1440 ? t("agoHours", { n: Math.round(minutes / 60) }) : t("agoDays", { n: Math.round(minutes / 1440) });
}

/** The host and path of a URL, without https://. */
export function shortUrl(url: string): string {
  return url.replace(/^https?:\/\//, "").replace(/\/$/, "");
}

/** 2.4 MB, 312 KB. */
function size(bytes: number, locale: Locale): string {
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });

  return bytes >= 1_000_000 ? `${number.format(bytes / 1_000_000)} MB` : `${number.format(Math.max(1, Math.round(bytes / 1000)))} KB`;
}

/** What the connection does, one line per direction. */
export function Flow({ t, connection }: { t: T; connection: ConnectionItem }) {
  const { webhook, calls, goals, login, sites } = connection;
  const loginKinds = { oidc: "OpenID Connect", ldap: "LDAP", handover: t("handover") } as const;

  return (
    <div className="connection-flow">
      {webhook !== null && (
        <span>
          <b>{t("weCall")}</b> · {webhook.events.join(", ")} → {shortUrl(webhook.url)}
          {webhook.surveys.length > 0 && ` · ${webhook.surveys.map((survey) => survey.title).join(", ")}`}
        </span>
      )}
      {calls !== null && (
        <span>
          <b>{t("theyCall")}</b> · {calls.signIn === "oauth" ? t("oauthClient") : t("apiKey")} · {calls.permissions.map((permission) => t(permissionKeys[permission].short)).join(", ")}
        </span>
      )}
      {goals.includes("sign-in") && (
        <span>
          <b>{t("goalSignIn")}</b> · {login === null ? t("signInNotYet") : [loginKinds[login.kind], login.host].filter(Boolean).join(" · ")}
        </span>
      )}
      {goals.includes("embed") && (
        <span>
          <b>{t("embedded")}</b> · {sites.length === 0 ? t("noSitesYet") : sites.join(", ")} · {t("handoverWho")}
        </span>
      )}
    </div>
  );
}

/** Whether it works: a coloured label, one line that says how much or why, and the last 7 days as bars. */
export function Health({ t, locale, connection, now, bars = false }: { t: T; locale: Locale; connection: ConnectionItem; now: Date; bars?: boolean }) {
  const day = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short" });
  const time = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
  const { health, reason, webhook, calls } = connection;
  const tone = health === "works" ? "ok" : health === "failing" || reason === "calls-refused" ? "bad" : health === "warning" ? "warn" : "off";
  const label =
    reason === "calls-refused"
      ? connection.refusedThisWeek === 1
        ? t("refusedOne")
        : t("refusedMany", { count: connection.refusedThisWeek })
      : t(({ works: "healthWorks", waiting: "healthWaiting", warning: "healthWarning", failing: "healthFailing", paused: "healthPaused" } as const)[health]);
  const days = calls?.expiresAt == null ? null : Math.max(0, Math.ceil((Date.parse(calls.expiresAt) - now.getTime()) / 86_400_000));
  const usual = () => {
    if (connection.lastExport !== null) {
      return [t("lastExport", { time: time.format(new Date(connection.lastExport.at)) }), size(connection.lastExport.size, locale)].join(" · ");
    }

    const parts = [
      webhook === null ? null : t("deliveriesWeek", { count: webhook.deliveredThisWeek }),
      days !== null && days <= 14 ? (days === 1 ? t("keyExpiresOne") : t("keyExpires", { days })) : null,
      connection.lastActivityAt === null ? null : webhook !== null ? t("lastWhen", { when: ago(t, connection.lastActivityAt, now) }) : t("lastCall", { when: ago(t, connection.lastActivityAt, now) }),
    ].filter((part): part is string => part !== null);

    return parts.length === 0 ? t("waitingDetail") : parts.join(" · ");
  };
  const detail =
    health === "paused"
      ? t("pausedDetail", { date: day.format(new Date(connection.pausedAt ?? connection.createdAt)), name: connection.pausedBy ?? "—" })
      : reason === "credentials-ended"
        ? t("reasonEnded")
        : reason === "deliveries-failing"
          ? t("reasonFailing", { date: day.format(new Date(webhook?.failingSince ?? connection.createdAt)) })
          : reason === "webhook-paused"
            ? t("reasonWebhookPaused")
            : reason === "deliveries-failed"
              ? t("reasonFailed", { count: webhook?.failedThisWeek ?? 0 })
              : usual();
  const top = Math.max(1, ...connection.days.map((entry) => entry.ok + entry.failed));
  const active = connection.days.some((entry) => entry.ok + entry.failed > 0);

  return (
    <span className="connection-health" data-tone={tone}>
      <b>
        <span className="connection-health__dot" aria-hidden="true" />
        {label}
      </b>
      <span>{detail}</span>
      {bars && health !== "paused" && active && (
        <span className="connection-health__bars" role="img" aria-label={t("lastSevenDays")}>
          {connection.days.map((entry) => (
            <i key={entry.day} data-failed={entry.failed > 0 ? "" : undefined} style={{ height: `${4 + Math.round((22 * (entry.ok + entry.failed)) / top)}px` }} />
          ))}
        </span>
      )}
    </span>
  );
}

/** The secrets, once: each masked until shown, each to copy; the client id is not a secret. */
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
      <h3 className="connection-kicker">{t("credentials")}</h3>
      <dl>
        {rows.map(([label, value, secret]) => (
          <Secret key={label} t={t} label={t(label)} value={value} secret={secret} writeClipboard={writeClipboard} />
        ))}
      </dl>
      <p className="connection-warn">{t("credentialsHint")}</p>
    </section>
  );
}

function Secret({ t, label, value, secret, writeClipboard }: { t: T; label: string; value: string; secret: boolean; writeClipboard: (text: string) => Promise<void> }) {
  const [shown, setShown] = useState(!secret);
  const [copied, setCopied] = useState(false);
  // whsec_ keys show their kind; the rest of a secret is hidden until asked.
  const prefix = value.startsWith("whsec_") ? "whsec_" : "";

  return (
    <div className="connection-secret">
      <dt>{label}</dt>
      <dd>
        <code aria-label={label}>{shown ? value : `${prefix}${"•".repeat(prefix === "" ? 32 : 22)}`}</code>
        {secret && prefix === "" && (
          <button type="button" className="designer-button connection-small" onClick={() => setShown(!shown)}>
            {shown ? t("hide") : t("show")}
          </button>
        )}
        <button type="button" className="designer-button connection-small" aria-label={`${t("copy")}: ${label}`} onClick={() => void writeClipboard(value).then(() => setCopied(true), () => undefined)}>
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
        {highlighted(sampleOf(language, setup))}
      </pre>
    </section>
  );
}

const keywords = new Set(["using", "var", "await", "new", "if", "return", "import", "from", "const", "for", "foreach", "in", "not", "of"]);

/** Comments, strings and keywords coloured as in the mockup; plain text otherwise, never HTML. */
function highlighted(code: string) {
  return code.split("\n").map((line, index) => {
    const comment = /^\s*(\/\/|#)/.test(line);
    const parts = comment ? [line] : line.split(/("[^"]*"|'[^']*'|\b[a-z]+\b)/);

    return (
      <span key={index}>
        {comment ? (
          <span className="c">{line}</span>
        ) : (
          parts.map((part, at) => (part.startsWith('"') || part.startsWith("'") ? <span key={at} className="s">{part}</span> : keywords.has(part) ? <span key={at} className="k">{part}</span> : part))
        )}
        {"\n"}
      </span>
    );
  });
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

/** The guide that fits a connection: connecting a CRM, webhooks, or exports. */
export function guideOf(connection: Pick<ConnectionItem, "goals" | "webhook">): { id: string; label: ConnectionsMessageKey } {
  return connection.goals.includes("people") ? { id: "crm", label: "guideCrm" } : connection.webhook !== null ? { id: "webhooks", label: "guideWebhooks" } : { id: "exports", label: "guideExports" };
}
