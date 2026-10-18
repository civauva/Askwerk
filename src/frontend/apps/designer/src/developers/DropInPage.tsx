// SPDX-License-Identifier: AGPL-3.0-only
import "../admin/admin.css";
import "./dropin.css";
import type { DomainsClient, HostLookSettings } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useEffect, useId, useMemo, useState } from "react";
import { dropInMessages, type DropInMessageKey } from "./dropInMessages";
import { PageBar } from "../shell/PageBar";

type Component = "survey" | "surveys" | "designer" | "results";
type LookMode = "host" | "askwerk" | "survey";

const components: { id: Component; tag: string; text: DropInMessageKey }[] = [
  { id: "survey", tag: "<askwerk-survey>", text: "componentSurvey" },
  { id: "surveys", tag: "<askwerk-surveys>", text: "componentSurveys" },
  { id: "designer", tag: "<askwerk-designer>", text: "componentDesigner" },
  { id: "results", tag: "<askwerk-results>", text: "componentResults" },
];

type DropInPageProps = {
  domains: Pick<DomainsClient, "embedding" | "designerEmbedding" | "hostLook" | "setHostLook">;
  locale: Locale;
  baseUrl?: string;
  onNavigate: (path: string) => void;
  writeClipboard: (text: string) => Promise<void>;
};

/** The tags and the CSS a host app writes, for the chosen components and look. */
export function dropInCode(baseUrl: string, chosen: readonly Component[], mode: LookMode, look: HostLookSettings): string {
  const lines = [`<script src="${baseUrl}/s/components.js" type="module"></script>`, ""];
  const lookAttribute = mode === "host" ? "" : ` look="${mode}"`;

  if (mode === "host" && (look.accent || look.background || look.font || look.radius != null)) {
    lines.push("<style>", "  askwerk-survey, askwerk-surveys, askwerk-designer, askwerk-results, askwerk-result {");
    look.accent && lines.push(`    --askwerk-accent: ${look.accent};`);
    look.background && lines.push(`    --askwerk-background: ${look.background};`);
    look.font && lines.push(`    --askwerk-font: ${look.font};`);
    look.radius != null && lines.push(`    --askwerk-radius: ${look.radius}px;`);
    lines.push("  }", "</style>", "");
  }

  const tags: Record<Component, string> = {
    survey: `<askwerk-survey survey="CODE" identity="TOKEN_FROM_YOUR_SERVER"${lookAttribute}></askwerk-survey>`,
    surveys: `<askwerk-surveys token="TOKEN_FROM_YOUR_SERVER"${lookAttribute}></askwerk-surveys>`,
    designer: `<askwerk-designer token="TOKEN_FROM_YOUR_SERVER"${lookAttribute}></askwerk-designer>`,
    results: `<askwerk-results token="TOKEN_FROM_YOUR_SERVER"${lookAttribute}></askwerk-results>\n<askwerk-result token="TOKEN_FROM_YOUR_SERVER" question="SAT"${lookAttribute}></askwerk-result>`,
  };

  return [...lines, ...chosen.map((each) => tags[each])].join("\n");
}

/**
 * Developers & API → Drop-in components (W37, AWK-50): which components, the websites that may show them, the host
 * look they follow, and the code for the host app.
 */
export function DropInPage({ domains, locale, baseUrl = location.origin, onNavigate, writeClipboard }: DropInPageProps) {
  const t = useMemo(() => createTranslator(dropInMessages, locale), [locale]);
  const id = useId();
  const [chosen, setChosen] = useState<Component[]>(["survey", "surveys", "designer", "results"]);
  const [sites, setSites] = useState<string[] | null>(null);
  const [mode, setMode] = useState<LookMode>("host");
  const [look, setLook] = useState<HostLookSettings>({ accent: null, background: null, font: null, radius: null });
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    void Promise.all([domains.embedding(), domains.designerEmbedding()]).then(([survey, designer]) => setSites([...new Set([...survey, ...designer].map((origin) => origin.replace(/^https?:\/\//, "")))]), () => setSites([]));
    void domains.hostLook().then((kept) => kept !== null && setLook(kept), () => undefined);
  }, [domains]);

  const save = () =>
    domains.setHostLook(look).then(
      (kept) => {
        setLook(kept);
        setNotice(t("saved"));
      },
      () => setNotice(t("invalid")),
    );
  const code = dropInCode(baseUrl, chosen, mode, look);

  return (
    <main className="admin admin--wide dropin" lang={locale}>
      <PageBar path={[{ place: "administration" }, { place: "developers", to: "/developers" }]} title={t("title")} />
      <h1 className="dropin__title">{t("title")}</h1>
      <p className="dropin__lead">{t("lead")}</p>

      <section className="dropin-card" aria-labelledby={`${id}-which`}>
        <h2 id={`${id}-which`}>{t("which")}</h2>
        <div className="dropin-components">
          {components.map((component) => (
            <button
              key={component.id}
              type="button"
              aria-pressed={chosen.includes(component.id)}
              onClick={() => setChosen((current) => (current.includes(component.id) ? current.filter((each) => each !== component.id) : components.map((each) => each.id).filter((each) => each === component.id || current.includes(each))))}
            >
              <code>{component.tag}</code>
              {t(component.text)}
            </button>
          ))}
        </div>
      </section>

      <section className="dropin-card" aria-labelledby={`${id}-where`}>
        <h2 id={`${id}-where`}>{t("where")}</h2>
        <div className="dropin-rows">
          <b>{t("sites")}</b>
          <span>
            {sites === null ? "…" : sites.length === 0 ? t("noSites") : sites.join(" · ")}{" "}
            <button type="button" className="designer-link-button dropin-muted" onClick={() => onNavigate("/domains")}>
              {t("fromDomains")}
            </button>
          </span>
          <b id={`${id}-look`}>{t("look")}</b>
          <span role="radiogroup" aria-labelledby={`${id}-look`} className="dropin-segment">
            {(
              [
                ["host", "lookHost"],
                ["askwerk", "lookAskwerk"],
                ["survey", "lookSurvey"],
              ] as const
            ).map(([value, label]) => (
              <label key={value} data-on={mode === value ? "" : undefined}>
                <input type="radio" name={`${id}-mode`} checked={mode === value} onChange={() => setMode(value)} />
                {t(label)}
              </label>
            ))}
          </span>
          {mode === "host" && (
            <>
              <b>{t("hostLook")}</b>
              <span className="dropin-look">
                <label>
                  <input type="color" value={look.accent ?? "#1456b8"} onChange={(event) => setLook({ ...look, accent: event.target.value })} />
                  {t("accent")}
                </label>
                <label>
                  <input type="color" value={look.background ?? "#f3f5f9"} onChange={(event) => setLook({ ...look, background: event.target.value })} />
                  {t("background")}
                </label>
                <label>
                  {t("font")}
                  <input className="designer-input" value={look.font ?? ""} placeholder="Segoe UI, sans-serif" onChange={(event) => setLook({ ...look, font: event.target.value || null })} />
                </label>
                <label>
                  {t("corners")}
                  <input className="designer-input dropin-number" type="number" min={0} max={24} value={look.radius ?? ""} onChange={(event) => setLook({ ...look, radius: event.target.value === "" ? null : Number(event.target.value) })} />
                  px
                </label>
                <span className="dropin-muted">{t("orFromPage")}</span>
                <button type="button" className="designer-button designer-button--primary" onClick={() => void save()}>
                  {t("save")}
                </button>
              </span>
            </>
          )}
          <b>{t("who")}</b>
          <span>
            {t("whoText")}{" "}
            <button type="button" className="designer-link-button" onClick={() => onNavigate("/connections")}>
              {t("connections")}
            </button>
          </span>
        </div>
        {notice !== null && (
          <p role="status" className="dropin-muted">
            {notice}
          </p>
        )}
      </section>

      <section className="dropin-card" aria-labelledby={`${id}-code`}>
        <h2 id={`${id}-code`}>{t("code")}</h2>
        <pre tabIndex={0} className="dropin-code">
          {code}
        </pre>
        <button type="button" className="designer-button" onClick={() => void writeClipboard(code).then(() => setNotice(t("copied")), () => undefined)}>
          {t("copy")}
        </button>
      </section>
    </main>
  );
}
