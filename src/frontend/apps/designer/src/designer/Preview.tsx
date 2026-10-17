// SPDX-License-Identifier: AGPL-3.0-only
import { Menu, MenuItem } from "@askwerk/ui";
import { useEffect, useRef, useState, type ReactNode, type Ref } from "react";
import { DeviceSwitch, deviceWidth, ZoomBox, type Device } from "./ZoomBox";
/** The texts the preview needs; the designer's and the results' translators both have them. */
export type PreviewKey =
  | "preview"
  | "previewSidePanel"
  | "previewNewTab"
  | "previewPdf"
  | "previewBanner"
  | "previewWidth"
  | "previewPhone"
  | "previewDesktop"
  | "previewTablet"
  | "previewZoom"
  | "previewZoomIn"
  | "previewZoomOut"
  | "previewFit"
  | "previewRestart"
  | "previewClose"
  | "previewFrame"
  | "previewSaved";

type T = (key: PreviewKey) => string;

/** The draft as a printable PDF in a language (AWK-17 step 7). */
export const pdfUrl = (surveyId: string, language: string, host?: string) =>
  `/api/app/surveys/${encodeURIComponent(surveyId)}/pdf?language=${encodeURIComponent(language)}${host === undefined ? "" : `&host=${encodeURIComponent(host)}`}`;

/** Where the runner shows a survey's draft to the organisation's members (AWK-22, AWK-27); nothing is stored. */
export const previewUrl = (surveyId: string) => `/s/${encodeURIComponent(surveyId)}?preview`;

/** Preview ▾ in the top bar (W9 B): in a side panel, or in a new tab. */
export function PreviewMenu({ t, onSidePanel, onNewTab, onPdf }: { t: T; onSidePanel: () => void; onNewTab: () => void; onPdf: () => void }) {
  return (
    <Menu
      label={t("preview")}
      trigger={(props) => (
        <button type="button" className="designer-button designer-button--quiet" {...props}>
          {t("preview")} <span aria-hidden="true">▾</span>
        </button>
      )}
    >
      <MenuItem onSelect={onSidePanel} hint={<kbd className="designer-kbd">Ctrl Shift P</kbd>}>
        {t("previewSidePanel")}
      </MenuItem>
      <MenuItem onSelect={onNewTab} hint="↗">
        {t("previewNewTab")}
      </MenuItem>
      <MenuItem onSelect={onPdf} hint="↓">
        {t("previewPdf")}
      </MenuItem>
    </Menu>
  );
}

/** What Look shows in the docked Preview besides the web (W26): the invitation email and the PDF, after each save. */
export type PreviewViews = {
  readonly labels: {
    readonly web: string;
    readonly email: string;
    readonly pdf: string;
    readonly shows: string;
    readonly emailFrame: string;
    readonly pdfFrame: string;
    /** "Page 2 of 4" in the stage bar (W32). */
    readonly pageOf: (page: number, pages: number) => string;
    readonly welcome: string;
    readonly thanks: string;
    readonly newTab: string;
  };
  readonly email?: () => Promise<string>;
  readonly pdf: string;
  /** Changes after each save, so the email and PDF are drawn again. */
  readonly saves: number;
};

/** Where the runner in the frame is, as it tells the Preview (W32): a page of so many, the welcome or the end. */
type Place = { readonly at: number | "welcome" | "end"; readonly pages: number };

/**
 * The draft in the real runner beside the designer, at phone, tablet or desktop width (W9 B, M5 B). The one place to
 * see the survey: over the editor, or docked in Look (W26, W32) as the stage beside the settings, with the look being
 * edited sent to its frame.
 */
export function PreviewPanel({
  t,
  surveyId,
  onClose,
  approval,
  docked = false,
  frameRef,
  onFrameLoad,
  views,
}: {
  t: T;
  surveyId: string;
  onClose: () => void;
  /** Approve… (AWK-28, W16 A). */
  approval?: ReactNode;
  docked?: boolean;
  frameRef?: Ref<HTMLIFrameElement>;
  onFrameLoad?: () => void;
  views?: PreviewViews;
}) {
  const [device, setDevice] = useState<Device>("desktop");
  // A new key loads the runner again: Restart starts from the welcome page with the last saved draft.
  const [run, setRun] = useState(0);
  const [view, setView] = useState<"web" | "email" | "pdf">("web");
  const [email, setEmail] = useState<string | null>(null);
  const [place, setPlace] = useState<Place | null>(null);
  const frame = useRef<HTMLIFrameElement | null>(null);
  const setFrame = (element: HTMLIFrameElement | null) => {
    frame.current = element;

    if (typeof frameRef === "function") {
      frameRef(element);
    } else if (frameRef !== null && frameRef !== undefined) {
      (frameRef as { current: HTMLIFrameElement | null }).current = element;
    }
  };

  useEffect(() => {
    if (view === "email" && views?.email !== undefined) {
      views.email().then(setEmail, () => setEmail(""));
    }
  }, [view, views?.email, views?.saves]);

  // The runner in the frame says where it is, for "Page 2 of 4" in the bar.
  useEffect(() => {
    const received = (event: MessageEvent) => {
      const data = event.data as { type?: string; at?: unknown; pages?: unknown } | null;

      if (event.origin === window.location.origin && event.source === frame.current?.contentWindow && data?.type === "askwerk:preview-page" && typeof data.pages === "number") {
        setPlace({ at: data.at === "end" || typeof data.at === "number" ? data.at : "welcome", pages: data.pages });
      }
    };
    window.addEventListener("message", received);

    return () => window.removeEventListener("message", received);
  }, []);

  const goto = (target: "welcome" | "end") => frame.current?.contentWindow?.postMessage({ type: "askwerk:goto", target }, window.location.origin);
  const zoomTexts = { zoom: t("previewZoom"), zoomIn: t("previewZoomIn"), zoomOut: t("previewZoomOut"), fit: t("previewFit") };
  const devices = <DeviceSwitch label={t("previewWidth")} texts={{ phone: t("previewPhone"), tablet: t("previewTablet"), desktop: t("previewDesktop") }} value={device} onChange={setDevice} />;
  const shown = (
    <>
      {view === "web" && <iframe ref={setFrame} key={run} title={t("previewFrame")} src={previewUrl(surveyId)} onLoad={onFrameLoad} />}
      {view === "email" && views !== undefined && <iframe key={`email-${views.saves}`} title={views.labels.emailFrame} sandbox="" srcDoc={email ?? ""} />}
      {view === "pdf" && views !== undefined && <iframe key={`pdf-${views.saves}`} title={views.labels.pdfFrame} src={views.pdf} />}
    </>
  );

  if (docked) {
    // The stage bar (W32): what to show, at which width, where the survey is, and the survey in a tab of its own.
    const bar = (
      <>
        {views !== undefined && (
          <span className="aw-stage__seg" role="group" aria-label={views.labels.shows}>
            <button type="button" aria-pressed={view === "web"} onClick={() => setView("web")}>
              {views.labels.web}
            </button>
            {views.email !== undefined && (
              <button type="button" aria-pressed={view === "email"} onClick={() => setView("email")}>
                {views.labels.email}
              </button>
            )}
            <button type="button" aria-pressed={view === "pdf"} onClick={() => setView("pdf")}>
              {views.labels.pdf}
            </button>
          </span>
        )}
        {devices}
        <span className="aw-stage__grow" />
        {views !== undefined && view === "web" && (
          <span className="aw-stage__pages">
            {place !== null && typeof place.at === "number" && `${views.labels.pageOf(place.at, place.pages)} · `}
            <button type="button" className="aw-stage__link" aria-current={place?.at === "welcome" ? "page" : undefined} onClick={() => goto("welcome")}>
              {views.labels.welcome}
            </button>
            {" · "}
            <button type="button" className="aw-stage__link" aria-current={place?.at === "end" ? "page" : undefined} onClick={() => goto("end")}>
              {views.labels.thanks}
            </button>
          </span>
        )}
        {views !== undefined && (
          <button type="button" className="aw-stage__button" onClick={() => window.open(previewUrl(surveyId), "_blank", "noopener")}>
            <span aria-hidden="true">↗</span> {views.labels.newTab}
          </button>
        )}
      </>
    );

    return (
      <aside className="designer-preview designer-preview--docked" aria-label={t("preview")}>
        <ZoomBox texts={zoomTexts} bar={bar} width={deviceWidth(device)}>
          {shown}
        </ZoomBox>
      </aside>
    );
  }

  return (
    <aside className="designer-preview" aria-label={t("preview")}>
      <div className="designer-preview__head">
        <span className="designer-preview__tag">{t("previewBanner")}</span>
        <span className="designer-bar__grow" />
        {approval}
        <button type="button" className="designer-preview__close" aria-label={t("previewClose")} onClick={onClose}>
          ×
        </button>
        <span className="designer-preview__tools">
          {devices}
          <button type="button" className="designer-button designer-button--quiet" onClick={() => setRun((current) => current + 1)}>
            {t("previewRestart")}
          </button>
          <span className="designer-note">{t("previewSaved")}</span>
        </span>
      </div>
      <div className="designer-preview__stage">
        <ZoomBox texts={zoomTexts} width={deviceWidth(device)}>
          {shown}
        </ZoomBox>
      </div>
    </aside>
  );
}

