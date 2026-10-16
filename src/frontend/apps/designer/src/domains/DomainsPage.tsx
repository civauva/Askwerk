// SPDX-License-Identifier: AGPL-3.0-only
import "../designer/designer.css";
import "./domains.css";
import { ApiError, type DomainItem, type DomainList, type DomainsClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { RadioGroup, Skeleton } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { domainsMessages, type DomainsMessageKey } from "./messages";

type T = (key: DomainsMessageKey, values?: Record<string, string | number>) => string;

type DomainsPageProps = {
  client: DomainsClient;
  locale: Locale;
  writeClipboard?: (text: string) => Promise<void>;
  timeZone?: string;
  now?: () => Date;
};

const addErrors: Record<string, DomainsMessageKey> = {
  "hostname-invalid": "hostnameInvalid",
  "hostname-not-subdomain": "hostnameNotSubdomain",
  "hostname-is-system": "hostnameIsSystem",
  "domain-taken": "domainTaken",
};

/**
 * Organisation → Domains (AWK-7, W12 A), one screen: the system address, the organisation's own domains with their
 * DNS records to copy and their state, one primary, and a field to add another.
 */
export function DomainsPage({ client, locale, writeClipboard = (text) => navigator.clipboard.writeText(text), now = () => new Date() }: DomainsPageProps) {
  const t: T = useMemo(() => createTranslator(domainsMessages, locale), [locale]);
  const [list, setList] = useState<DomainList | null | "failed">(null);
  const [error, setError] = useState<string | null>(null);

  const reload = () => client.list().then(setList, () => setList("failed"));

  // Loads once per client; reload is recreated on every render.
  useEffect(() => {
    void reload();
  }, [client]);

  const act = (work: Promise<unknown>) => {
    setError(null);
    work.then(reload, () => setError(t("actionFailed")));
  };

  return (
    <main className="domains" lang={locale}>
      <h1>{t("title")}</h1>
      <p className="domains-lead">{t("lead")}</p>
      {list === null && (
        <div role="status" aria-busy="true" className="domains-loading">
          <span className="domains-hidden">{t("loading")}</span>
          <Skeleton width="100%" height="64px" />
          <Skeleton width="100%" height="64px" />
        </div>
      )}
      {list === "failed" && <p role="alert">{t("failedToLoad")}</p>}
      {list !== null && list !== "failed" && (
        <>
          {error !== null && <p role="alert">{error}</p>}
          <section className="domains-row" aria-label={list.system.hostname}>
            <div className="domains-row__head">
              <h2>{list.system.hostname}</h2>
              <span className="domains-pill">{t("system")}</span>
              <span className="domains-pill">{t("ready")} ✓</span>
              {list.system.isPrimary && <span className="domains-pill domains-pill--strong">{t("primary")}</span>}
              <span className="domains-grow" />
              {!list.system.isPrimary && (
                <button type="button" className="designer-button designer-button--quiet" onClick={() => act(client.setPrimary(null))}>
                  {t("makePrimary")}
                </button>
              )}
            </div>
            <p className="domains-hint">{t("systemHint")}</p>
          </section>
          {list.items.map((domain) => (
            <DomainRow key={domain.id} t={t} domain={domain} client={client} writeClipboard={writeClipboard} now={now} onChanged={reload} onAct={act} />
          ))}
          <AddDomain t={t} client={client} onAdded={reload} />
          <Embedding t={t} client={client} />
          <DesignerEmbedding t={t} client={client} />
        </>
      )}
    </main>
  );
}

function ago(t: T, at: string, now: Date) {
  const minutes = Math.floor((now.getTime() - new Date(at).getTime()) / 60_000);

  return minutes < 1 ? t("justNow") : minutes < 60 ? t("minutesAgo", { count: minutes }) : t("hoursAgo", { count: Math.floor(minutes / 60) });
}

function DomainRow({
  t,
  domain,
  client,
  writeClipboard,
  now,
  onChanged,
  onAct,
}: {
  t: T;
  domain: DomainItem;
  client: DomainsClient;
  writeClipboard: (text: string) => Promise<void>;
  now: () => Date;
  onChanged: () => void;
  onAct: (work: Promise<unknown>) => void;
}) {
  const [checking, setChecking] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [copied, setCopied] = useState<string | null>(null);
  const status = domain.status === "verified" ? t("verified") : domain.status === "pending" ? t("pending") : t("failed");

  const verify = () => {
    setChecking(true);
    client.verify(domain.id).then(
      () => {
        setChecking(false);
        onChanged();
      },
      () => setChecking(false),
    );
  };

  const copy = (type: string, text: string) => {
    void writeClipboard(text).then(() => setCopied(type));
  };

  return (
    <section className={domain.isPrimary ? "domains-row domains-row--primary" : "domains-row"} aria-label={domain.hostname}>
      <div className="domains-row__head">
        <h2>{domain.hostname}</h2>
        {domain.isPrimary && <span className="domains-pill domains-pill--strong">{t("primary")}</span>}
        <span className="domains-pill">{status}</span>
        <span className="domains-grow" />
        {domain.status === "verified" && !domain.isPrimary && (
          <button type="button" className="designer-button designer-button--quiet" onClick={() => onAct(client.setPrimary(domain.id))}>
            {t("makePrimary")}
          </button>
        )}
        {!removing && (
          <button type="button" className="designer-button designer-button--quiet" onClick={() => setRemoving(true)}>
            {t("remove")}
          </button>
        )}
      </div>
      {removing && (
        <div className="domains-confirm">
          <p>{t("removeQuestion", { hostname: domain.hostname })}</p>
          <button type="button" className="designer-button designer-button--quiet" onClick={() => setRemoving(false)}>
            {t("cancel")}
          </button>
          <button type="button" className="designer-button domains-danger" onClick={() => onAct(client.remove(domain.id))}>
            {t("removeDomain")}
          </button>
        </div>
      )}
      {domain.status === "verified" && <p className="domains-hint">{t("verifiedHint")}</p>}
      {domain.status !== "verified" && (
        <>
          <p className="domains-hint">{domain.status === "pending" ? t("pendingHint") : t("failedHint")}</p>
          <table className="domains-records" aria-label={t("records")}>
            <thead>
              <tr>
                <th scope="col">{t("type")}</th>
                <th scope="col">{t("name")}</th>
                <th scope="col">{t("value")}</th>
                <th scope="col">
                  <span className="domains-hidden">{t("copy")}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {(
                [
                  ["CNAME", domain.hostname, domain.cnameTarget],
                  ["TXT", domain.txtName, domain.txtValue],
                ] as const
              ).map(([type, name, value]) => (
                <tr key={type}>
                  <td>{type}</td>
                  <td>{name}</td>
                  <td>{value}</td>
                  <td>
                    <button type="button" className="designer-button designer-button--quiet" aria-label={t("copyValue", { type })} onClick={() => copy(type, value)}>
                      {copied === type ? t("copied") : t("copy")}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="domains-check">
            {domain.checkedAt === null
              ? t("notChecked")
              : t("lastCheck", { when: ago(t, domain.checkedAt, now()), cname: domain.cnameFound ? t("found") : t("notFound"), txt: domain.txtFound ? t("found") : t("notFound") })}
          </p>
          <button type="button" className="designer-button domains-primary" disabled={checking} onClick={verify}>
            {checking ? t("checking") : domain.status === "failed" ? t("verifyAgain") : t("verify")}
          </button>
        </>
      )}
    </section>
  );
}

function AddDomain({ t, client, onAdded }: { t: T; client: DomainsClient; onAdded: () => void }) {
  const id = useId();
  const [hostname, setHostname] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  const add = () => {
    setAdding(true);
    setError(null);
    client.add(hostname).then(
      () => {
        setAdding(false);
        setHostname("");
        onAdded();
      },
      (failure: unknown) => {
        setAdding(false);
        const code = failure instanceof ApiError ? (failure.problem.errors?.["hostname"]?.[0] ?? failure.problem.code) : undefined;
        setError(t(addErrors[code ?? ""] ?? "actionFailed"));
      },
    );
  };

  return (
    <form
      className="domains-add"
      onSubmit={(event) => {
        event.preventDefault();
        add();
      }}
    >
      <label htmlFor={id}>{t("add")}</label>
      <div className="domains-add__line">
        <input
          id={id}
          type="text"
          inputMode="url"
          autoComplete="off"
          spellCheck={false}
          placeholder={t("addPlaceholder")}
          value={hostname}
          aria-invalid={error !== null}
          aria-describedby={`${id}-hint`}
          onChange={(event) => setHostname(event.target.value)}
        />
        <button type="submit" className="designer-button domains-primary" disabled={adding || hostname.trim() === ""}>
          {t("addButton")}
        </button>
      </div>
      {error !== null && <p role="alert">{error}</p>}
      <p id={`${id}-hint`} className="domains-hint">
        {t("addHint")}
      </p>
    </form>
  );
}

/** The websites that may show the surveys in a frame (AWK-7, W12 B note 5); none switches embedding off. */
function Embedding({ t, client }: { t: T; client: DomainsClient }) {
  const id = useId();
  const [text, setText] = useState<string | null>(null);
  const [state, setState] = useState<"idle" | "saving" | "saved" | "invalid" | "failed">("idle");

  useEffect(() => {
    client.embedding().then(
      (origins) => setText(origins.join("\n")),
      () => setText(""),
    );
  }, [client]);

  if (text === null) {
    return null;
  }

  const save = () => {
    setState("saving");
    const origins = text.split("\n").map((line) => line.trim()).filter((line) => line !== "");
    client.setEmbedding(origins).then(
      () => setState("saved"),
      (failure: unknown) => setState(failure instanceof ApiError && failure.status === 400 ? "invalid" : "failed"),
    );
  };

  return (
    <div className="domains-embed">
      <h2 id={`${id}-title`}>{t("embedTitle")}</h2>
      <p className="domains-hint">{t("embedLead")}</p>
      <label htmlFor={id} className="domains-hidden">
        {t("embedTitle")}
      </label>
      <textarea
        id={id}
        rows={3}
        spellCheck={false}
        placeholder="https://www.your-company.com"
        value={text}
        aria-invalid={state === "invalid"}
        onChange={(event) => {
          setText(event.target.value);
          setState("idle");
        }}
      />
      <div className="domains-add__line">
        <button type="button" className="designer-button designer-button--quiet" disabled={state === "saving"} onClick={save}>
          {t("embedSave")}
        </button>
        {state === "saved" && <span className="domains-check">{t("embedSaved")}</span>}
      </div>
      {state === "invalid" && <p role="alert">{t("embedInvalid")}</p>}
      {state === "failed" && <p role="alert">{t("actionFailed")}</p>}
    </div>
  );
}

/**
 * Embedding of the designer and Results in a host product (AWK-13, W21): off by default; on, only on the websites
 * listed. The host signs people in with the hand-over of Organisation → Sign-in.
 */
function DesignerEmbedding({ t, client }: { t: T; client: DomainsClient }) {
  const id = useId();
  const [text, setText] = useState<string | null>(null);
  const [on, setOn] = useState(false);
  const [state, setState] = useState<"idle" | "saving" | "saved" | "invalid" | "failed">("idle");

  useEffect(() => {
    client.designerEmbedding().then(
      (origins) => {
        setText(origins.join("\n"));
        setOn(origins.length > 0);
      },
      () => setText(""),
    );
  }, [client]);

  if (text === null) {
    return null;
  }

  const save = () => {
    setState("saving");
    const origins = on ? text.split("\n").map((line) => line.trim()).filter((line) => line !== "") : [];
    client.setDesignerEmbedding(origins).then(
      () => setState("saved"),
      (failure: unknown) => setState(failure instanceof ApiError && failure.status === 400 ? "invalid" : "failed"),
    );
  };

  return (
    <div className="domains-embed">
      <h2>{t("designerEmbedTitle")}</h2>
      <p className="domains-hint">{t("designerEmbedLead")}</p>
      <RadioGroup
        legend={t("designerEmbedTitle")}
        hideLegend
        value={on ? "on" : "off"}
        options={[
          { value: "off", label: t("designerEmbedOff") },
          { value: "on", label: t("designerEmbedOn") },
        ]}
        onChange={(value) => {
          setOn(value === "on");
          setState("idle");
        }}
      />
      {on && (
        <>
          <label htmlFor={id} className="domains-hidden">
            {t("designerEmbedOn")}
          </label>
          <textarea
            id={id}
            rows={2}
            spellCheck={false}
            placeholder="https://hr.your-company.com"
            value={text}
            aria-invalid={state === "invalid"}
            onChange={(event) => {
              setText(event.target.value);
              setState("idle");
            }}
          />
        </>
      )}
      <div className="domains-add__line">
        <button type="button" className="designer-button designer-button--quiet" disabled={state === "saving"} onClick={save}>
          {t("designerEmbedSave")}
        </button>
        {state === "saved" && <span className="domains-check">{t("embedSaved")}</span>}
      </div>
      {state === "invalid" && <p role="alert">{t("embedInvalid")}</p>}
      {state === "failed" && <p role="alert">{t("actionFailed")}</p>}
    </div>
  );
}
