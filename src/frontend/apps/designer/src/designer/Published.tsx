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
  /** The language being edited: the PDF uses it unless the link opens in a set language. */
  language: string;
  /** The survey's languages (W34): the link can open in one of them, or in the respondent's own. */
  languages?: readonly string[];
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
 * What a published survey offers (AWK-7, W12 B, W34): the link with its QR code first, then the other ways as equal
 * cards (email, a website, paper). The address and the "Opens in" language go into all of them.
 */
export function Published(props: PublishedProps) {
  // The server always gives the system address; nothing to show without a link.
  return props.links.length === 0 ? null : <Addresses {...props} />;
}

function Addresses({ t, surveyId, title, language, languages = [], links, writeClipboard, saveFile, participants, preview, locale = "en", embedSites }: PublishedProps) {
  const addressId = useId();
  const [hostname, setHostname] = useState(links[0]!.hostname);
  const [embed, setEmbed] = useState<"script" | "iframe">("script");
  const [copied, setCopied] = useState<string | null>(null);
  const [inviting, setInviting] = useState(false);
  const link = links.find((each) => each.hostname === hostname) ?? links[0]!;
  // The respondent's own language by default; a set language goes into the link, the QR code and the embed code.
  const [opensIn, setOpensIn] = useState<string | null>(null);
  const url = opensIn === null ? link.url : `${link.url}?lang=${opensIn}`;
  const origin = new URL(link.url).origin;
  const code = codeOf(link.url);
  const snippet =
    embed === "script"
      ? `<script src="${origin}/s/embed.js" data-survey="${code}"${opensIn === null ? "" : ` data-lang="${opensIn}"`} async></script>`
      : `<iframe src="${link.url}?embed${opensIn === null ? "" : `&lang=${opensIn}`}" title="${title.replaceAll('"', "&quot;")}" style="width:100%;height:720px;border:0" loading="lazy"></iframe>`;

  const copy = (what: string, text: string) => writeClipboard(text).then(() => setCopied(what), () => undefined);

  return (
    <div className="designer-published">
      {/* The link first, with its QR code beside it (W34): the way most people share. */}
      <section className="designer-share designer-share--link" aria-labelledby={addressId}>
        <div className="designer-share__main">
          <h3 id={addressId} className="designer-share__title">
            {t("linkTitle")}
          </h3>
          <p className="designer-published__hint">{t("linkHint")}</p>
          <span id={`${addressId}-address`} className="designer-visually-hidden">
            {t("address")}
          </span>
          <div className="designer-published__address">
            {links.length > 1 && (
              <ComboBox
                labelledBy={`${addressId}-address`}
                options={links.map((each) => ({ value: each.hostname, label: each.isPrimary ? t("addressPrimary", { hostname: each.hostname }) : each.hostname }))}
                value={link.hostname}
                onChange={setHostname}
                texts={{ choose: t("address") }}
              />
            )}
            <code>{url}</code>
            <button type="button" className="designer-button" aria-label={t("copyLink", { url })} onClick={() => void copy("link", url)}>
              {copied === "link" ? t("copied") : t("copy")}
            </button>
            <a className="designer-button designer-button--quiet" href={url} target="_blank" rel="noopener">
              {t("openLink")}
            </a>
          </div>
          {languages.length > 1 && (
            <div className="designer-share__lang">
              <span id={`${addressId}-lang`}>{t("opensIn")}</span>
              <span className="designer-segment" role="group" aria-labelledby={`${addressId}-lang`}>
                <button type="button" aria-pressed={opensIn === null} onClick={() => setOpensIn(null)}>
                  {t("respondentsChoice")}
                </button>
                {languages.map((each) => (
                  <button key={each} type="button" aria-pressed={opensIn === each} onClick={() => setOpensIn(each)}>
                    {each.toUpperCase()}
                  </button>
                ))}
              </span>
            </div>
          )}
        </div>
        <div className="designer-published__qr" aria-label={t("qrCode")} role="group">
          <QrCode value={url} label={t("qrFor", { url })} size={112} />
          <div>
            <button type="button" className="designer-link-button" onClick={() => void qrPng(url).then((png) => png !== null && saveFile(`${code}-qr.png`, png))}>
              {t("downloadPng")}
            </button>
            <button type="button" className="designer-link-button" onClick={() => saveFile(`${code}-qr.svg`, new Blob([qrSvg(url)], { type: "image/svg+xml" }))}>
              {t("downloadSvg")}
            </button>
          </div>
        </div>
      </section>
      {/* Then the other ways, as equal cards: email, a website, paper. */}
      <div className="designer-share__ways">
        {participants !== undefined && (
          <section className="designer-share" aria-labelledby={`${addressId}-invite`}>
            <h3 id={`${addressId}-invite`} className="designer-share__title">
              {t("personalInvitations")}
            </h3>
            <p className="designer-published__hint">{t("personalInvitationsHint")}</p>
            <div>
              <button type="button" className="designer-button" onClick={() => setInviting(true)}>
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
          </section>
        )}
        <section className="designer-share" aria-labelledby={`${addressId}-embed`}>
          <h3 id={`${addressId}-embed`} className="designer-share__title">
            {t("embed")}
          </h3>
          <p className="designer-published__hint">{t("embedHint")}</p>
          <Tabs label={t("embed")} tabs={[["script", t("embedScript")], ["iframe", "iframe"]]} value={embed} onChange={setEmbed} />
          <textarea className="designer-published__code" readOnly rows={4} aria-label={t("embedCode")} value={snippet} />
          <button type="button" className="designer-link-button" onClick={() => void copy("embed", snippet)}>
            {copied === "embed" ? t("copied") : t("copyEmbed")}
          </button>
          {embedSites !== undefined && <EmbedSites t={t} sites={embedSites} />}
        </section>
        <section className="designer-share" aria-labelledby={`${addressId}-paper`}>
          <h3 id={`${addressId}-paper`} className="designer-share__title">
            {t("onPaper")}
          </h3>
          <p className="designer-published__hint">{t("paperHint")}</p>
          <div>
            <a className="designer-button" href={pdfUrl(surveyId, opensIn ?? language, link.hostname)} target="_blank" rel="noopener">
              {t("downloadPdf")}
            </a>
          </div>
        </section>
      </div>
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
