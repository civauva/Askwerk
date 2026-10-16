// SPDX-License-Identifier: AGPL-3.0-only
import { Checkbox, encodeQr, Tabs } from "@askwerk/ui";
import { ComboBox, QrCode, qrSvg } from "@askwerk/ui";
import type { ParticipantsClient, SurveyEmbedding } from "@askwerk/app-client";
import type { Locale, Translate } from "@askwerk/schema";
import { useEffect, useId, useState } from "react";
import type { SurveyLink } from "./api";
import type { DesignerMessageKey } from "./messages";
import { InviteDialog } from "../participants/InviteDialog";
import { pdfUrl } from "./Preview";

type PublishedProps = {
  t: Translate<DesignerMessageKey>;
  surveyId: string;
  title: string;
  /** The language being edited: the PDF and the embed code use it. */
  language: string;
  links: readonly SurveyLink[];
  writeClipboard: (text: string) => Promise<void>;
  saveFile: (name: string, file: Blob) => void;
  /** Invitations (AWK-7); "Invite participants…" shows when given. */
  participants?: ParticipantsClient | undefined;
  preview?: ((language: string) => Promise<{ subject: string; text: string }>) | undefined;
  locale?: Locale;
  /** The survey's own websites (AWK-13, W21 3); "Only on these sites" shows when given. */
  embedSites?: { load: () => Promise<SurveyEmbedding>; save: (origins: readonly string[]) => Promise<void> } | undefined;
};

/** The survey's short code: the last segment of its link. */
const codeOf = (url: string) => new URL(url).pathname.split("/").filter(Boolean).pop() ?? "";

/** A QR code as a PNG: 10 pixels per module, black on white; null where the browser has no canvas. */
function qrPng(value: string): Promise<Blob | null> {
  const modules = encodeQr(value);
  const scale = 10;
  const extent = (modules.length + 8) * scale;
  const canvas = document.createElement("canvas");
  canvas.width = extent;
  canvas.height = extent;
  const context = canvas.getContext("2d");

  if (context === null) {
    return Promise.resolve(null);
  }

  context.fillStyle = "#ffffff";
  context.fillRect(0, 0, extent, extent);
  context.fillStyle = "#000000";
  modules.forEach((row, y) => row.forEach((dark, x) => dark && context.fillRect((x + 4) * scale, (y + 4) * scale, scale, scale)));

  return new Promise((resolve) => canvas.toBlob(resolve, "image/png"));
}

/**
 * What a published survey offers (AWK-7, W12 B): one address switch, and the link, QR code, embed code and PDF all
 * follow it. The primary address comes first.
 */
export function Published(props: PublishedProps) {
  // The server always gives the system address; nothing to show without a link.
  return props.links.length === 0 ? null : <Addresses {...props} />;
}

function Addresses({ t, surveyId, title, language, links, writeClipboard, saveFile, participants, preview, locale = "en", embedSites }: PublishedProps) {
  const addressId = useId();
  const [hostname, setHostname] = useState(links[0]!.hostname);
  const [embed, setEmbed] = useState<"script" | "iframe">("script");
  const [copied, setCopied] = useState<string | null>(null);
  const [inviting, setInviting] = useState(false);
  const link = links.find((each) => each.hostname === hostname) ?? links[0]!;
  const origin = new URL(link.url).origin;
  const code = codeOf(link.url);
  const snippet =
    embed === "script"
      ? `<script src="${origin}/s/embed.js" data-survey="${code}" data-lang="${language}" async></script>`
      : `<iframe src="${link.url}?embed&lang=${language}" title="${title.replaceAll('"', "&quot;")}" style="width:100%;height:720px;border:0" loading="lazy"></iframe>`;

  const copy = (what: string, text: string) => writeClipboard(text).then(() => setCopied(what), () => undefined);

  return (
    <div className="designer-published">
      <p className="designer-published__label" id={addressId}>
        {t("address")}
      </p>
      <div className="designer-published__address">
        {links.length > 1 ? (
          <ComboBox
            labelledBy={addressId}
            options={links.map((each) => ({ value: each.hostname, label: each.isPrimary ? t("addressPrimary", { hostname: each.hostname }) : each.hostname }))}
            value={link.hostname}
            onChange={setHostname}
            texts={{ choose: t("address") }}
          />
        ) : (
          <span className="designer-chip">{link.hostname}</span>
        )}
        <code>{link.url}</code>
        <button type="button" className="designer-button" aria-label={t("copyLink", { url: link.url })} onClick={() => void copy("link", link.url)}>
          {copied === "link" ? t("copied") : t("copy")}
        </button>
      </div>
      <div className="designer-published__grid">
        <section aria-labelledby={`${addressId}-qr`}>
          <h3 id={`${addressId}-qr`} className="designer-published__label">
            {t("qrCode")}
          </h3>
          <div className="designer-published__qr">
            <QrCode value={link.url} label={t("qrFor", { url: link.url })} size={120} />
            <div>
              <button type="button" className="designer-link-button" onClick={() => void qrPng(link.url).then((png) => png !== null && saveFile(`${code}-qr.png`, png))}>
                {t("downloadPng")}
              </button>
              <button
                type="button"
                className="designer-link-button"
                onClick={() => saveFile(`${code}-qr.svg`, new Blob([qrSvg(link.url)], { type: "image/svg+xml" }))}
              >
                {t("downloadSvg")}
              </button>
              <p className="designer-published__hint">{t("qrHint")}</p>
            </div>
          </div>
        </section>
        <section aria-labelledby={`${addressId}-embed`}>
          <h3 id={`${addressId}-embed`} className="designer-published__label">
            {t("embed")}
          </h3>
          <Tabs label={t("embed")} tabs={[["script", t("embedScript")], ["iframe", "iframe"]]} value={embed} onChange={setEmbed} />
          <textarea className="designer-published__code" readOnly rows={4} aria-label={t("embedCode")} value={snippet} />
          <button type="button" className="designer-link-button" onClick={() => void copy("embed", snippet)}>
            {copied === "embed" ? t("copied") : t("copyEmbed")}
          </button>
          <p className="designer-published__hint">{t("embedHint")}</p>
          {embedSites !== undefined && <EmbedSites t={t} sites={embedSites} />}
        </section>
      </div>
      <h3 className="designer-published__label">{t("onPaper")}</h3>
      <a className="designer-link-button" href={pdfUrl(surveyId, language, link.hostname)} target="_blank" rel="noopener">
        {t("downloadPdf")}
      </a>
      {participants !== undefined && (
        <>
          <h3 className="designer-published__label">{t("personalInvitations")}</h3>
          <p className="designer-published__hint">{t("personalInvitationsHint")}</p>
          <div>
            <button type="button" className="designer-button designer-button--primary" onClick={() => setInviting(true)}>
              {t("inviteParticipants")}
            </button>
          </div>
          {inviting && (
            <InviteDialog
              client={participants}
              surveyId={surveyId}
              locale={locale}
              host={link.hostname}
              {...(preview === undefined ? {} : { preview })}
              onClose={() => setInviting(false)}
            />
          )}
        </>
      )}
    </div>
  );
}

/** Share → Embed → "Only on these sites": some of the organisation's websites; none ticked allows them all. */
function EmbedSites({ t, sites }: { t: Translate<DesignerMessageKey>; sites: NonNullable<PublishedProps["embedSites"]> }) {
  const [choice, setChoice] = useState<SurveyEmbedding | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    sites.load().then(setChoice, () => setChoice(null));
  }, [sites]);

  if (choice === null || choice.organisation.length < 2) {
    return null;
  }

  const toggle = (origin: string, on: boolean) => {
    const origins = choice.organisation.filter((each) => (each === origin ? on : choice.origins.includes(each)));
    setChoice({ ...choice, origins });
    setSaved(false);
    void sites.save(origins).then(() => setSaved(true), () => undefined);
  };

  return (
    <details className="designer-published__sites">
      <summary>{t("embedOnlySites")}</summary>
      <p className="designer-published__hint">{t("embedOnlySitesHint")}</p>
      {choice.organisation.map((origin) => (
        <Checkbox key={origin} label={origin} checked={choice.origins.includes(origin)} onChange={(on) => toggle(origin, on)} />
      ))}
      {saved && <span role="status">{t("embedSitesSaved")}</span>}
    </details>
  );
}
