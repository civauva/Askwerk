// SPDX-License-Identifier: AGPL-3.0-only
import "./developers.css";
import { ApiError, type DevelopersClient, type DevelopersInfo, type TrialKey } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ConfirmDialog } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { guides, withBaseUrl } from "./guides";
import { Markdown } from "./markdown";
import { developersMessages, type DevelopersMessageKey } from "./messages";

type T = (key: DevelopersMessageKey, values?: Record<string, string | number>) => string;

/** One operation of the public API, from its OpenAPI description. */
export type Operation = {
  readonly id: string;
  readonly method: string;
  readonly path: string;
  readonly area: string;
  readonly summary: string;
  readonly description: string;
  readonly scope: string | undefined;
  readonly parameters: readonly { name: string; in: "path" | "query"; required: boolean; description: string }[];
  readonly body: boolean;
};

const methods = ["get", "post", "put", "patch", "delete"] as const;

/** "PublicResponses" → "Responses", as the Postman collection names its folders. */
export const areaOf = (tag: string) => tag.replace(/^Public(?=.)/, "").replace(/Controller$/, "");

/** The operations of an OpenAPI description, in its order. */
export function operationsOf(openApi: unknown): Operation[] {
  const paths = (openApi as { paths?: Record<string, Record<string, Record<string, unknown>>> } | null)?.paths ?? {};

  return Object.entries(paths).flatMap(([path, item]) =>
    methods.flatMap((method) => {
      const operation = item[method] as
        | { tags?: string[]; summary?: string; description?: string; parameters?: { name: string; in: string; required?: boolean; description?: string }[]; requestBody?: unknown }
        | undefined;

      if (operation === undefined) {
        return [];
      }

      const description = operation.description ?? "";

      return [
        {
          id: `${method} ${path}`,
          method: method.toUpperCase(),
          path,
          area: areaOf(operation.tags?.[0] ?? "API"),
          summary: operation.summary ?? `${method.toUpperCase()} ${path}`,
          description,
          scope: /Scope ([a-z]+:[a-z]+)/.exec(description)?.[1],
          parameters: (operation.parameters ?? [])
            .filter((parameter) => parameter.in === "path" || parameter.in === "query")
            .map((parameter) => ({ name: parameter.name, in: parameter.in as "path" | "query", required: parameter.required === true, description: parameter.description ?? "" })),
          body: operation.requestBody !== undefined,
        },
      ];
    }),
  );
}

type View = { kind: "start" } | { kind: "guide"; id: string } | { kind: "reference"; area: string } | { kind: "tools" };

export type DevelopersPageProps = {
  client: DevelopersClient;
  locale: Locale;
  /** Owners and admins create keys, a trial key included. */
  canManageKeys: boolean;
  onOpenApiKeys: () => void;
  /** Calls the public API for Try it; the browser's fetch by default. */
  fetcher?: typeof fetch;
  writeClipboard?: (text: string) => Promise<void>;
};

/**
 * The Developers hub (AWK-34, W27): this Askwerk's API address, guides to fit it into other systems, the API reference
 * with Try it, SDKs, samples, and downloads with the address built in. Every member reads it.
 */
export function DevelopersPage({ client, locale, canManageKeys, onOpenApiKeys, fetcher = (input, init) => fetch(input, init), writeClipboard = (text) => navigator.clipboard.writeText(text) }: DevelopersPageProps) {
  const t: T = useMemo(() => createTranslator(developersMessages, locale), [locale]);
  const [info, setInfo] = useState<DevelopersInfo | null>(null);
  const [operations, setOperations] = useState<Operation[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [view, setView] = useState<View>({ kind: "start" });
  const [trial, setTrial] = useState<TrialKey | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    client.info().then(setInfo, () => setFailed(true));
    client.openApi().then((document) => setOperations(operationsOf(document)), () => setFailed(true));
  }, [client]);

  const areas = useMemo(() => [...new Set((operations ?? []).map((operation) => operation.area))], [operations]);

  const createTrial = () =>
    client.trialKey().then(
      (created) => {
        setTrial(created);
        setView({ kind: "reference", area: areas.includes("Surveys") ? "Surveys" : (areas[0] ?? "Surveys") });
      },
      (error: unknown) => setNotice(error instanceof ApiError && error.status === 403 ? t("notAllowed") : t("failed")),
    );

  const copy = (text: string) => void writeClipboard(text).then(() => setNotice(t("copied")), () => undefined);

  if (failed && info === null) {
    return (
      <main className="dev" aria-label={t("hub")}>
        <p role="alert">{t("failed")}</p>
      </main>
    );
  }

  if (info === null || operations === null) {
    return <main className="dev" aria-label={t("hub")} aria-busy="true" />;
  }

  const baseUrl = info.baseUrl;
  const link = (target: View, label: string, current: boolean) => (
    <a
      href="#"
      aria-current={current ? "page" : undefined}
      onClick={(event) => {
        event.preventDefault();
        setView(target);
        setNotice(null);
      }}
    >
      {label}
    </a>
  );

  return (
    <div className="dev">
      <nav className="dev-side" aria-label={t("hub")}>
        {link({ kind: "start" }, t("start"), view.kind === "start")}
        <h2 className="dev-side__head">{t("guidesNav")}</h2>
        {guides.map((guide) => link({ kind: "guide", id: guide.id }, t(guide.title), view.kind === "guide" && view.id === guide.id))}
        <h2 className="dev-side__head">{t("referenceNav")}</h2>
        {areas.map((area) => link({ kind: "reference", area }, area, view.kind === "reference" && view.area === area))}
        <h2 className="dev-side__head">{t("toolsNav")}</h2>
        {link({ kind: "tools" }, t("tools"), view.kind === "tools")}
        {canManageKeys && (
          <a
            href="/api-keys"
            onClick={(event) => {
              event.preventDefault();
              onOpenApiKeys();
            }}
          >
            {t("yourKeys")}
          </a>
        )}
      </nav>
      <main className="dev-main" aria-label={t("hub")}>
        {notice !== null && (
          <p className="dev-notice" role="status">
            {notice}
          </p>
        )}
        {view.kind === "start" && (
          <Start t={t} info={info} canManageKeys={canManageKeys} postmanUrl={client.postmanUrl} openApiUrl={client.openApiUrl} onCopy={copy} onTrial={() => void createTrial()} onTryIt={() => setView({ kind: "reference", area: areas[0] ?? "" })} onGuide={(id) => setView({ kind: "guide", id })} />
        )}
        {view.kind === "guide" && (
          <article className="dev-article">
            <h1 className="dev-title">{t(guides.find((guide) => guide.id === view.id)!.title)}</h1>
            <Markdown source={withBaseUrl(guides.find((guide) => guide.id === view.id)!.source, baseUrl)} />
          </article>
        )}
        {view.kind === "reference" && (
          <Reference key={view.area} t={t} area={view.area} operations={operations.filter((operation) => operation.area === view.area)} baseUrl={baseUrl} trial={trial} locale={locale} fetcher={fetcher} onCopy={copy} />
        )}
        {view.kind === "tools" && <Tools t={t} client={client} operations={operations} areas={areas.length} />}
      </main>
    </div>
  );
}

function Start({
  t,
  info,
  canManageKeys,
  postmanUrl,
  openApiUrl,
  onCopy,
  onTrial,
  onTryIt,
  onGuide,
}: {
  t: T;
  info: DevelopersInfo;
  canManageKeys: boolean;
  postmanUrl: string;
  openApiUrl: string;
  onCopy: (text: string) => void;
  onTrial: () => void;
  onTryIt: () => void;
  onGuide: (id: string) => void;
}) {
  return (
    <>
      <h1 className="dev-title">{t("title")}</h1>
      <p className="dev-lead">{t("lead")}</p>
      <div className="dev-base">
        <span className="dev-label">{t("baseUrl")}</span>
        <code>{info.apiUrl}</code>
        <button type="button" className="dev-button" onClick={() => onCopy(info.apiUrl)}>
          {t("copy")}
        </button>
        <a className="dev-button" href={postmanUrl} download>
          {t("postman")}
        </a>
        <a className="dev-button" href={openApiUrl} download="askwerk-openapi.json">
          {t("openApi")}
        </a>
      </div>
      <h2 className="dev-h2">{t("firstCall")}</h2>
      <ol className="dev-steps">
        <li className="dev-card">
          <b>{t("stepKey")}</b>
          <span className="dev-muted">{canManageKeys ? t("stepKeyText") : t("stepKeyOther")}</span>
          {canManageKeys && (
            <button type="button" className="dev-button" onClick={onTrial}>
              {t("createTrial")}
            </button>
          )}
        </li>
        <li className="dev-card">
          <b>{t("stepCall")}</b>
          <span className="dev-muted">{t("stepCallText")}</span>
          <pre tabIndex={0} className="dev-code">{`curl -H "Authorization: Bearer $KEY" \\\n  ${info.apiUrl}/surveys`}</pre>
          <button type="button" className="dev-button" onClick={onTryIt}>
            {t("tryIt")}
          </button>
        </li>
        <li className="dev-card">
          <b>{t("stepSync")}</b>
          <span className="dev-muted">{t("stepSyncText")}</span>
          <button type="button" className="dev-button" onClick={() => onGuide("webhooks")}>
            {t("readGuide")}
          </button>
        </li>
      </ol>
      <h2 className="dev-h2">{t("fit")}</h2>
      <ul className="dev-guides">
        {guides.map((guide) => (
          <li key={guide.id}>
            <button type="button" className="dev-card dev-guide" onClick={() => onGuide(guide.id)}>
              <span className="dev-tag">{t(guide.tag)}</span>
              <b>{t(guide.title)}</b>
              <span className="dev-muted">{t(guide.summary)}</span>
            </button>
          </li>
        ))}
      </ul>
    </>
  );
}

type Result = { status: number; ok: boolean; ms: number; body: string };

/** The request as curl, with this Askwerk's address. */
export function curlOf(operation: Operation, url: string, body: string | null): string {
  const lines = [`curl -X ${operation.method} "${url}"`, `  -H "Authorization: Bearer $ASKWERK_KEY"`];

  if (body !== null) {
    lines.push(`  -H "Content-Type: application/json"`, `  -d '${body.replace(/'/g, "'\\''")}'`);
  }

  return lines.join(" \\\n");
}

function exampleOf(language: "curl" | "typescript" | "csharp" | "python", operation: Operation, url: string, body: string | null): string {
  switch (language) {
    case "curl":
      return curlOf(operation, url, body);
    case "typescript":
      return `const answer = await fetch("${url}", {\n  method: "${operation.method}",\n  headers: { Authorization: \`Bearer \${process.env.ASKWERK_KEY}\`${body !== null ? `, "Content-Type": "application/json"` : ""} },${body !== null ? `\n  body: JSON.stringify(${body.trim() || "{}"}),` : ""}\n});\nconsole.log(answer.status, await answer.json());`;
    case "csharp":
      return `using var http = new HttpClient();\nhttp.DefaultRequestHeaders.Authorization = new("Bearer", Environment.GetEnvironmentVariable("ASKWERK_KEY"));\nusing var request = new HttpRequestMessage(HttpMethod.${operation.method[0]}${operation.method.slice(1).toLowerCase()}, "${url}");${body !== null ? `\nrequest.Content = new StringContent("""${body.trim() || "{}"}""", System.Text.Encoding.UTF8, "application/json");` : ""}\nvar answer = await http.SendAsync(request);\nConsole.WriteLine(await answer.Content.ReadAsStringAsync());`;
    default:
      return `import json, os, urllib.request\n\nrequest = urllib.request.Request("${url}", method="${operation.method}", headers={"Authorization": "Bearer " + os.environ["ASKWERK_KEY"]${body !== null ? `, "Content-Type": "application/json"` : ""}}${body !== null ? `, data=json.dumps(${body.trim() || "{}"}).encode()` : ""})\nwith urllib.request.urlopen(request) as answer:\n    print(json.load(answer))`;
  }
}

function Reference({ t, area, operations, baseUrl, trial, locale, fetcher, onCopy }: { t: T; area: string; operations: Operation[]; baseUrl: string; trial: TrialKey | null; locale: Locale; fetcher: typeof fetch; onCopy: (text: string) => void }) {
  const [chosen, setChosen] = useState(operations[0]?.id ?? "");
  const operation = operations.find((each) => each.id === chosen) ?? operations[0];

  return (
    <div className="dev-reference">
      <div className="dev-endpoints">
        <h1 className="dev-title">{area}</h1>
        <ul aria-label={t("endpoints")}>
          {operations.map((each) => (
            <li key={each.id}>
              <button type="button" className="dev-endpoint" aria-pressed={each.id === operation?.id} onClick={() => setChosen(each.id)}>
                <span className={`dev-method dev-method--${each.method.toLowerCase()}`}>{each.method}</span>
                <span className="dev-path">{each.path.replace(/^\/api\/v1/, "")}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="dev-muted">{t("realData")}</p>
      </div>
      {operation !== undefined && <TryIt key={operation.id} t={t} operation={operation} baseUrl={baseUrl} trial={trial} locale={locale} fetcher={fetcher} onCopy={onCopy} />}
    </div>
  );
}

function TryIt({ t, operation, baseUrl, trial, locale, fetcher, onCopy }: { t: T; operation: Operation; baseUrl: string; trial: TrialKey | null; locale: Locale; fetcher: typeof fetch; onCopy: (text: string) => void }) {
  const id = useId();
  const [values, setValues] = useState<Record<string, string>>({});
  const [body, setBody] = useState("{\n}");
  const [key, setKey] = useState(trial?.secret ?? "");
  const [language, setLanguage] = useState<"curl" | "typescript" | "csharp" | "python">("curl");
  const [result, setResult] = useState<Result | null>(null);
  const [busy, setBusy] = useState(false);
  const [asking, setAsking] = useState(false);

  const path = operation.path.replace(/\{([^}]+)\}/g, (_, name: string) => encodeURIComponent(values[name] ?? "") || `{${name}}`);
  const query = new URLSearchParams(operation.parameters.filter((parameter) => parameter.in === "query" && (values[parameter.name] ?? "") !== "").map((parameter) => [parameter.name, values[parameter.name]!]));
  const relative = `${path}${query.size > 0 ? `?${query}` : ""}`;
  const url = `${baseUrl}${relative}`;
  const sentBody = operation.body ? body : null;

  const send = async () => {
    setAsking(false);
    setBusy(true);
    const started = performance.now();

    try {
      const answer = await fetcher(relative, {
        method: operation.method,
        headers: { Authorization: `Bearer ${key.trim()}`, ...(sentBody !== null && { "Content-Type": "application/json" }) },
        ...(sentBody !== null && { body: sentBody }),
        credentials: "omit",
      });
      const text = await answer.text();
      let shown = text;

      try {
        shown = JSON.stringify(JSON.parse(text), null, 2);
      } catch {
        // Not JSON: shown as it came.
      }

      setResult({ status: answer.status, ok: answer.ok, ms: Math.round(performance.now() - started), body: shown });
    } catch (error) {
      setResult({ status: 0, ok: false, ms: Math.round(performance.now() - started), body: String(error) });
    } finally {
      setBusy(false);
    }
  };

  const expires = trial?.key.expiresAt === null || trial === null ? null : new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" }).format(new Date(trial.key.expiresAt));

  return (
    <>
      <section className="dev-card dev-try" aria-labelledby={`${id}-title`}>
        <h2 id={`${id}-title`} className="dev-op">
          <span className={`dev-method dev-method--${operation.method.toLowerCase()}`}>{operation.method}</span> <code>{operation.path}</code>
        </h2>
        <p className="dev-muted">
          {operation.summary}
          {operation.scope !== undefined && ` ${t("scope", { scope: operation.scope })}.`}
        </p>
        {operation.parameters.length > 0 && <h3 className="dev-label">{t("parameters")}</h3>}
        {operation.parameters.map((parameter) => (
          <div key={parameter.name} className="dev-field">
            <label htmlFor={`${id}-${parameter.name}`}>{parameter.name}</label>
            <input
              id={`${id}-${parameter.name}`}
              className="dev-input"
              placeholder={parameter.required ? t("required") : t("optional")}
              title={parameter.description}
              value={values[parameter.name] ?? ""}
              onChange={(event) => setValues((current) => ({ ...current, [parameter.name]: event.target.value }))}
            />
          </div>
        ))}
        {operation.body && (
          <div className="dev-field dev-field--wide">
            <label htmlFor={`${id}-body`}>{t("body")}</label>
            <textarea id={`${id}-body`} className="dev-input dev-body" rows={5} value={body} onChange={(event) => setBody(event.target.value)} />
          </div>
        )}
        <h3 className="dev-label">{t("key")}</h3>
        <div className="dev-field">
          <label htmlFor={`${id}-key`}>{t("bearer")}</label>
          <input id={`${id}-key`} className="dev-input" type="password" autoComplete="off" value={key} onChange={(event) => setKey(event.target.value)} />
        </div>
        <p className="dev-muted">{trial !== null && key === trial.secret && expires !== null ? t("trialNote", { time: expires }) : t("keyNote")}</p>
        <div className="dev-row">
          <button type="button" className="dev-button dev-button--primary" disabled={busy || key.trim() === ""} onClick={() => (operation.method === "GET" ? void send() : setAsking(true))}>
            {busy ? t("sending") : t("send")}
          </button>
          <button type="button" className="dev-button" onClick={() => onCopy(curlOf(operation, url, sentBody))}>
            {t("copyCurl")}
          </button>
        </div>
        <h3 className="dev-label">{t("example")}</h3>
        <div className="dev-segment" role="group" aria-label={t("example")}>
          {(
            [
              ["curl", "curl"],
              ["typescript", "TypeScript"],
              ["csharp", "C#"],
              ["python", "Python"],
            ] as const
          ).map(([value, label]) => (
            <button key={value} type="button" aria-pressed={language === value} onClick={() => setLanguage(value)}>
              {label}
            </button>
          ))}
        </div>
        <pre tabIndex={0} className="dev-code">{exampleOf(language, operation, url, sentBody)}</pre>
      </section>
      <section className="dev-card dev-result" aria-label={t("response")} aria-live="polite">
        {result === null ? (
          <p className="dev-muted">{t("noResponse")}</p>
        ) : (
          <>
            <p className="dev-status">
              <b className={result.ok ? "dev-ok" : "dev-bad"}>{result.status === 0 ? "—" : result.status}</b> <span className="dev-muted">· {t("ms", { ms: result.ms })}</span>
            </p>
            <pre tabIndex={0} className="dev-code dev-code--tall">{result.body}</pre>
          </>
        )}
      </section>
      {asking && <ConfirmDialog title={t("writeTitle")} text={t("writeAsk", { method: operation.method })} confirm={t("send")} cancel={t("cancel")} onCancel={() => setAsking(false)} onConfirm={() => void send()} />}
    </>
  );
}

function Tools({ t, client, operations, areas }: { t: T; client: DevelopersClient; operations: Operation[]; areas: number }) {
  return (
    <>
      <h1 className="dev-title">{t("tools")}</h1>
      <div className="dev-three">
        <div className="dev-card">
          <span className="dev-label">{t("postmanTitle")}</span>
          <span className="dev-muted">{t("postmanText", { count: operations.length, folders: areas })}</span>
          <a className="dev-button dev-button--primary" href={client.postmanUrl} download>
            {t("postmanButton")}
          </a>
        </div>
        <div className="dev-card">
          <span className="dev-label">{t("openApiTitle")}</span>
          <span className="dev-muted">{t("openApiText")}</span>
          <a className="dev-button" href={client.openApiUrl} download="askwerk-openapi.json">
            {t("openApiButton")}
          </a>
        </div>
        <div className="dev-card">
          <span className="dev-label">{t("exampleTitle")}</span>
          <b>{t("exampleName")}</b>
          <span className="dev-muted">{t("exampleText")}</span>
          <a className="dev-button" href={client.exampleUrl} download>
            {t("exampleButton")}
          </a>
        </div>
      </div>
      <h2 className="dev-h2">{t("sdks")}</h2>
      <div className="dev-three">
        <div className="dev-card">
          <b>TypeScript</b>
          <pre tabIndex={0} className="dev-code">npm install @askwerk/sdk</pre>
          <span className="dev-muted">{t("sdkTs")}</span>
        </div>
        <div className="dev-card">
          <b>C# / .NET</b>
          <pre tabIndex={0} className="dev-code">dotnet add package Askwerk.Sdk</pre>
          <span className="dev-muted">{t("sdkCs")}</span>
        </div>
        <div className="dev-card">
          <b>Python</b>
          <pre tabIndex={0} className="dev-code">pip install askwerk</pre>
          <span className="dev-muted">{t("sdkPy")}</span>
        </div>
      </div>
      <h2 className="dev-h2">{t("samples")}</h2>
      <div className="dev-three">
        <div className="dev-card">
          <b>{t("sampleNightly")}</b>
          <span className="dev-muted">{t("sampleNightlyText")}</span>
          <pre tabIndex={0} className="dev-code">{`const page = await askwerk.responses.list(surveyId, { since });\nawait db.insert(page.items);\nsince = page.nextSince;`}</pre>
        </div>
        <div className="dev-card">
          <b>{t("sampleInvite")}</b>
          <span className="dev-muted">{t("sampleInviteText")}</span>
          <pre tabIndex={0} className="dev-code">{`await askwerk.users.put("emp-4711", {\n  email: "anna@acme.at", name: "Anna Berger", role: "editor",\n});`}</pre>
        </div>
        <div className="dev-card">
          <b>{t("sampleWebhook")}</b>
          <span className="dev-muted">{t("sampleWebhookText")}</span>
          <pre tabIndex={0} className="dev-code">{`if (!(await verifyWebhook(secret, headers["askwerk-signature"], body))) return 401;\nqueue.add(JSON.parse(body));\nreturn 204;`}</pre>
        </div>
      </div>
    </>
  );
}
