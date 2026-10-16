// SPDX-License-Identifier: AGPL-3.0-only
import { Menu, MenuItem } from "@askwerk/ui";
import { useEffect, useState, type ReactNode, type Ref } from "react";
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
  readonly labels: { readonly web: string; readonly email: string; readonly pdf: string; readonly shows: string; readonly emailFrame: string; readonly pdfFrame: string };
  readonly email?: () => Promise<string>;
  readonly pdf: string;
  /** Changes after each save, so the email and PDF are drawn again. */
  readonly saves: number;
};

/**
 * The draft in the real runner beside the designer, at desktop or phone width (W9 B, M5 B). The one place to see the
 * survey: over the editor, or docked in Look (W26) with the look being edited sent to its frame.
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
  const [phone, setPhone] = useState(false);
  // A new key loads the runner again: Restart starts from the welcome page with the last saved draft.
  const [run, setRun] = useState(0);
  const [view, setView] = useState<"web" | "email" | "pdf">("web");
  const [email, setEmail] = useState<string | null>(null);

  useEffect(() => {
    if (view === "email" && views?.email !== undefined) {
      views.email().then(setEmail, () => setEmail(""));
    }
  }, [view, views?.email, views?.saves]);

  return (
    <aside className={docked ? "designer-preview designer-preview--docked" : "designer-preview"} aria-label={t("preview")}>
      <div className="designer-preview__head">
        <span className="designer-preview__tag">{t("previewBanner")}</span>
        <span className="designer-bar__grow" />
        {approval}
        {!docked && (
          <button type="button" className="designer-preview__close" aria-label={t("previewClose")} onClick={onClose}>
            ×
          </button>
        )}
        <span className="designer-preview__tools">
          {views !== undefined && (
            <span className="designer-segment" role="group" aria-label={views.labels.shows}>
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
          {view === "web" && (
            <>
              <span className="designer-segment" role="group" aria-label={t("previewWidth")}>
                <button type="button" aria-pressed={phone} onClick={() => setPhone(true)}>
                  {t("previewPhone")}
                </button>
                <button type="button" aria-pressed={!phone} onClick={() => setPhone(false)}>
                  {t("previewDesktop")}
                </button>
              </span>
              <button type="button" className="designer-button designer-button--quiet" onClick={() => setRun((current) => current + 1)}>
                {t("previewRestart")}
              </button>
              <span className="designer-note">{t("previewSaved")}</span>
            </>
          )}
        </span>
      </div>
      <div className="designer-preview__stage">
        {view === "web" && <iframe ref={frameRef} key={run} title={t("previewFrame")} src={previewUrl(surveyId)} style={{ width: phone ? "390px" : "100%" }} onLoad={onFrameLoad} />}
        {view === "email" && views !== undefined && <iframe key={`email-${views.saves}`} title={views.labels.emailFrame} sandbox="" srcDoc={email ?? ""} style={{ width: "100%" }} />}
        {view === "pdf" && views !== undefined && <iframe key={`pdf-${views.saves}`} title={views.labels.pdfFrame} src={views.pdf} style={{ width: "100%" }} />}
      </div>
    </aside>
  );
}
