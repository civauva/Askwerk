// SPDX-License-Identifier: AGPL-3.0-only
import type { ParticipantsClient, SurveySummary } from "@askwerk/app-client";
import "./designer.css";
import { ApiError } from "@askwerk/app-client";
import { checkDefinition, createTranslator, type Locale, type Translate } from "@askwerk/schema";
import { Skeleton, Dialog, DialogActions } from "@askwerk/ui";
import { useCallback, useDeferredValue, useEffect, useId, useMemo, useReducer, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useShellSlot } from "../shell/AppShell";
import { CommandPalette, type Command } from "./CommandPalette";
import type { DesignerApi, SurveyDraft } from "./api";
import { useAutosave } from "./autosave";
import { Canvas } from "./Canvas";
import { Images } from "./images";
import { Inspector } from "./Inspector";
import { designerMessages, questionTypeNames, type DesignerMessageKey } from "./messages";
import { Outline } from "./Outline";
import { SurveyLook } from "./look/SurveyLook";
import { lookMessages } from "./look/messages";
import { pdfUrl, PreviewPanel, previewUrl } from "./Preview";
import { PublishDialog } from "./PublishDialog";
import { allQuestions, designerReducer, initialDesignerState } from "./state";
import { displayText, languageName } from "./text";
import { approvalLine, ApprovalPill, ApprovalSlot, SurveyBar, SurveyPlaces, SurveyTabs, statusText as summaryText, useApprovals, workspaceText, type SurveyTab, type WorkspaceText } from "../workspace";
import type { SaveStatus } from "./autosave";
import { useShellView } from "../shell/AppShell";

type DesignerProps = {
  api: DesignerApi;
  surveyId: string;
  initialLocale: Locale;
  newId: () => string;
  autosaveDelayMs?: number;
  writeClipboard?: (text: string) => Promise<void>;
  /** Saves a file the browser made, e.g. a QR code (AWK-7). */
  saveFile?: (name: string, file: Blob) => void;
  /** Invitations from the published view (AWK-7). */
  participants?: ParticipantsClient;
  /** The invitation as people get it, for the invite dialog. */
  previewInvitation?: (surveyId: string, language: string) => Promise<{ subject: string; text: string }>;
  /** The invitation email's HTML in the survey's look, for the Preview in Look (W26). */
  previewEmail?: (surveyId: string, language: string) => Promise<string>;
  /** Back to the survey list. */
  onBack?: () => void;
  /** Goes to another part of the survey: Share, Results or Participants (AWK-29, W17). */
  onTab?: (tab: SurveyTab) => void;
  /** Opens another survey, e.g. a duplicate. */
  onOpenSurvey?: (surveyId: string) => void;
  /** Back to Templates when the survey is where a template is edited (W32). */
  onTemplates?: () => void;
  /** The profile menu at the bar's far right (W23). */
  profile?: ReactNode;
  /** Opens on Create → Look instead of Questions (W23, /surveys/{id}/look). */
  initialLook?: boolean;
  /** Opens a page in a new tab; the preview uses it (W9 C). */
  openWindow?: (url: string) => void;
};

/** The survey designer (AWK-15), built to the approved mockups M1–M3 in AWK-5. */
export function Designer({
  api,
  surveyId,
  initialLocale,
  newId,
  autosaveDelayMs = 800,
  writeClipboard = (text) => navigator.clipboard.writeText(text),
  saveFile = downloadFile,
  participants,
  previewInvitation,
  previewEmail,
  onBack,
  onTab,
  onOpenSurvey,
  onTemplates,
  profile,
  initialLook = false,
  openWindow = (url) => void window.open(url, "_blank", "noopener"),
}: DesignerProps) {
  const [draft, setDraft] = useState<SurveyDraft | null>(null);
  const t = createTranslator(designerMessages, initialLocale);

  useEffect(() => {
    api.loadDraft(surveyId).then(setDraft, () => undefined);
  }, [api, surveyId]);

  if (draft === null) {
    return (
      <div className="designer" lang={initialLocale}>
        <div role="status" aria-busy="true" aria-label={t("loading")} className="designer-loading">
          <Skeleton width="100%" height="56px" />
          <div className="designer-loading__regions">
            <Skeleton width="100%" height="100%" />
            <Skeleton width="100%" height="100%" />
            <Skeleton width="100%" height="100%" />
          </div>
        </div>
      </div>
    );
  }

  return (
    <Workspace
      api={api}
      draft={draft}
      locale={initialLocale}
      newId={newId}
      autosaveDelayMs={autosaveDelayMs}
      writeClipboard={writeClipboard}
      saveFile={saveFile}
      participants={participants}
      previewInvitation={previewInvitation}
      previewEmail={previewEmail}
      onBack={onBack}
      onTab={onTab}
      onOpenSurvey={onOpenSurvey}
      onTemplates={onTemplates}
      openWindow={openWindow}
      profile={profile}
      initialLook={initialLook}
    />
  );
}

type WorkspaceProps = {
  api: DesignerApi;
  onBack: (() => void) | undefined;
  onTab: ((tab: SurveyTab) => void) | undefined;
  onOpenSurvey: ((surveyId: string) => void) | undefined;
  onTemplates?: (() => void) | undefined;
  draft: SurveyDraft;
  locale: Locale;
  newId: () => string;
  autosaveDelayMs: number;
  writeClipboard: (text: string) => Promise<void>;
  saveFile: (name: string, file: Blob) => void;
  participants: ParticipantsClient | undefined;
  previewInvitation: ((surveyId: string, language: string) => Promise<{ subject: string; text: string }>) | undefined;
  previewEmail: ((surveyId: string, language: string) => Promise<string>) | undefined;
  openWindow: (url: string) => void;
  profile: ReactNode;
  initialLook: boolean;
};

function Workspace({ api, draft, locale, newId, autosaveDelayMs, writeClipboard, saveFile, participants, previewInvitation, previewEmail, onBack, onTab, onOpenSurvey, onTemplates, openWindow, profile, initialLook }: WorkspaceProps) {
  // Stable between renders, so unchanged question cards are not drawn again on every keystroke.
  const t = useMemo(() => createTranslator(designerMessages, locale), [locale]);
  const viewing = useShellView();
  const typeNames = questionTypeNames[locale];
  const [state, dispatch] = useReducer(designerReducer, draft, (loaded) => ({
    ...initialDesignerState(loaded.definition, loaded.definition.languages.includes(locale) ? locale : loaded.definition.defaultLanguage),
    settings: initialLook,
  }));
  // Each save draws the email and PDF in Look's preview again (W26).
  const [saves, setSaves] = useState(0);
  // On by default (owner, 2026-10-14); the switch on the canvas turns it off.
  const [grid, setGrid] = useState(true);
  const [publishing, setPublishing] = useState(false);
  const [palette, setPalette] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [savingTemplate, setSavingTemplate] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const w = useMemo(() => workspaceText(locale), [locale]);
  const [summary, setSummary] = useState<SurveySummary | null>(null);
  // The definition as last published from here; edits after it are changes to publish (W17).
  const [baseline, setBaseline] = useState(draft.definition);
  const loadSummary = useCallback(() => {
    api.summary(draft.surveyId).then(setSummary, () => undefined);
  }, [api, draft.surveyId]);

  useEffect(loadSummary, [loadSummary]);
  const [approvals, reloadApprovals] = useApprovals(api, draft.surveyId);

  // A move that would break logic is refused (AWK-31); the notice says why.
  useEffect(() => {
    if (state.notice) {
      setNotice(state.notice.kind === "logic-order" ? t("logicOrder", { used: state.notice.used, by: state.notice.by }) : t("logicJumpOrder"));
    }
  }, [state.notice, t]);

  const images = useMemo(() => ({ upload: (file: File) => api.uploadImage(file), url: (assetId: string, width?: number) => api.assetUrl(assetId, width) }), [api]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPalette(true);
      }

      if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === "p") {
        event.preventDefault();
        setPreviewing((current) => !current);
      }
    }

    document.addEventListener("keydown", onKeyDown);

    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  function commands(): Command[] {
    const { definition } = state;
    const selectedPage = definition.pages.find((page) => page.questions.some((question) => question.id === state.selectedQuestionId));
    const targetPage = selectedPage ?? definition.pages.at(-1);
    const after = selectedPage !== undefined ? state.selectedQuestionId : (targetPage?.questions.at(-1)?.id ?? null);
    const types = Object.keys(typeNames) as (keyof typeof typeNames)[];

    // Questions first: typing a word from a question finds it before a type that happens to contain it.
    return [
      ...allQuestions(definition).map((question) => ({
        id: `go-${question.id}`,
        label: t("cmdGoTo", { text: displayText(question.text, state.language, definition) ?? t("untitled"), variable: question.variable }),
        run: () => dispatch({ type: "select", questionId: question.id }),
      })),
      ...types.map((questionType) => ({
        id: `add-${questionType}`,
        label: t("cmdAdd", { type: typeNames[questionType] }),
        run: () =>
          targetPage !== undefined &&
          dispatch({ type: "addQuestion", questionType, pageId: targetPage.id, afterQuestionId: after, newId: newId() }),
      })),
      ...definition.languages.map((language) => ({
        id: `language-${language}`,
        label: t("cmdLanguage", { language: languageName(t, language) }),
        run: () => dispatch({ type: "setLanguage", language }),
      })),
      { id: "settings", label: t("look"), run: () => dispatch({ type: "openSettings" }) },
      { id: "preview", label: `${t("preview")}: ${t("previewSidePanel")}`, run: () => setPreviewing(true) },
      { id: "preview-tab", label: `${t("preview")}: ${t("previewNewTab")}`, run: () => openWindow(previewUrl(draft.surveyId)) },
      { id: "save-template", label: `${t("saveAsTemplate")}…`, run: () => setSavingTemplate(true) },
      { id: "publish", label: t("publish"), run: () => setPublishing(true) },
      { id: "grid", label: t("cmdGrid"), run: () => setGrid((current) => !current) },
      // Simple or Advanced from the palette too (W45, AWK-71).
      viewing.view === "simple"
        ? { id: "view-advanced", label: t("cmdViewAdvanced"), run: () => viewing.setView("advanced") }
        : { id: "view-simple", label: t("cmdViewSimple"), run: () => viewing.setView("simple") },
    ];
  }

  const save = useCallback((definition: SurveyDraft["definition"]) => api.saveDraft(draft.surveyId, definition), [api, draft.surveyId]);
  const { status, retryNow } = useAutosave(state.definition, save, {
    delayMs: autosaveDelayMs,
    retryMs: 8000,
    traceIdOf: (error) => (error instanceof ApiError ? error.traceId : undefined),
  });

  // An approval holds for the saved draft it was given for; after each save the server says whether it still does (W16).
  useEffect(() => {
    if (status.kind === "saved") {
      reloadApprovals();
      setSaves((count) => count + 1);
    }
  }, [status.kind, reloadApprovals]);

  // The count follows typing a moment later, so the checks never hold up a keystroke.
  const deferredDefinition = useDeferredValue(state.definition);
  const missing = useMemo(
    () => checkDefinition(deferredDefinition, draft.published ?? undefined).filter((issue) => issue.code === "translation-missing").length,
    [deferredDefinition, draft.published],
  );

  // Until the summary comes, the draft gives the name; edits since the last publish are changes to publish (W17).
  const shown: SurveySummary =
    summary === null
      ? { id: draft.surveyId, title: draft.title, status: "draft", publishedVersion: null, hasChanges: false, languages: [], responses: 0, participants: 0 }
      : { ...summary, hasChanges: summary.hasChanges || (summary.publishedVersion !== null && state.definition !== baseline) };

  const lt = createTranslator(lookMessages, locale);
  // Inside the app's sidebar (AWK-38, W29) the survey's places go there and the bar names the place.
  const slot = useShellSlot();
  const approvalSlot = (
    <ApprovalSlot
      t={w}
      approvals={approvals}
      title={shown.title}
      languages={state.definition.languages}
      onApprove={(note) => api.approve(draft.surveyId, note).then(reloadApprovals)}
      onWithdraw={(approvalId) => api.withdrawApproval(draft.surveyId, approvalId).then(reloadApprovals)}
    />
  );

  return (
    <div className="designer" lang={locale}>
      <SurveyBar
        t={w}
        surveys={api}
        surveyId={draft.surveyId}
        summary={shown}
        canEdit
        status={<span className={status.kind === "failed" ? "designer-meta--bad" : undefined}>{barStatus(t, w, status, summary === null ? null : shown)}</span>}
        languages={state.definition.languages}
        language={state.language}
        onLanguage={(language) => dispatch({ type: "setLanguage", language })}
        languageNote={missing > 0 && <span className="designer-meta">{missing === 1 ? t("missingOne") : t("missing", { count: missing })}</span>}
        onSearch={() => setPalette(true)}
        onPreview={(where) =>
          where === "panel" ? setPreviewing(true) : openWindow(where === "pdf" ? pdfUrl(draft.surveyId, state.language) : previewUrl(draft.surveyId))
        }
        onPrimary={(action) => (action === "share" ? onTab?.("share") : setPublishing(true))}
        onBack={() => onBack?.()}
        {...(onTemplates === undefined ? {} : { onTemplates })}
        {...(onOpenSurvey === undefined ? {} : { onOpenSurvey })}
        onChanged={loadSummary}
        {...(slot === null ? { profile } : { place: state.settings ? w("settings") : w("build"), onHome: () => dispatch({ type: "select", questionId: state.selectedQuestionId }) })}
        approval={approvals !== null && <ApprovalPill t={w} approvals={approvals} />}
      />
      {slot === null ? (
        <SurveyTabs
          t={w}
          active={state.settings ? "settings" : "build"}
          canEdit
          summary={summary === null ? null : shown}
          onSelect={(tab) => {
            if (tab === "settings") {
              dispatch({ type: "openSettings" });
            } else if (tab === "build") {
              dispatch({ type: "select", questionId: state.selectedQuestionId });
            } else {
              onTab?.(tab);
            }
          }}
        />
      ) : (
        createPortal(
          <SurveyPlaces
            t={w}
            active={state.settings ? "settings" : "build"}
            canEdit
            summary={summary === null ? null : shown}
            onSelect={(tab) => {
              if (tab === "settings") {
                dispatch({ type: "openSettings" });
              } else if (tab === "build") {
                dispatch({ type: "select", questionId: state.selectedQuestionId });
              } else {
                onTab?.(tab);
              }
            }}
          />,
          slot,
        )
      )}
      <Images.Provider value={images}>
      {state.settings ? (
        // Create → Look (W26): the look definition, with the one Preview docked beside it.
        <SurveyLook
          api={api}
          surveyId={draft.surveyId}
          locale={locale}
          definition={state.definition}
          language={state.language}
          onLayout={(layout) => dispatch({ type: "setLayout", layout })}
          onHeaderImage={(asset) => dispatch({ type: "setHeaderImage", asset })}
          onHeaderAlt={(language, value) => dispatch({ type: "setHeaderAlt", language, value })}
          renderPreview={(frame, onFrameLoad, lookSaves, mode) => (
            <PreviewPanel
              t={t}
              surveyId={draft.surveyId}
              onClose={() => undefined}
              docked
              frameRef={frame}
              onFrameLoad={onFrameLoad}
              views={{
                labels: {
                  web: lt("previewWeb"),
                  email: lt("previewEmail"),
                  pdf: lt("previewPdfTab"),
                  shows: lt("previewShows"),
                  emailFrame: lt("emailFrame"),
                  pdfFrame: lt("pdfFrame"),
                  pageOf: (page, pages) => lt("previewPageOf", { page, pages }),
                  welcome: lt("previewWelcome"),
                  thanks: lt("previewThanks"),
                  newTab: lt("previewOpenNewTab"),
                },
                ...(previewEmail !== undefined && { email: () => previewEmail(draft.surveyId, state.language) }),
                pdf: pdfUrl(draft.surveyId, state.language),
                saves: saves + lookSaves,
                ...(mode !== undefined && { mode: { ...mode, labels: { group: lt("previewMode"), edit: lt("previewEdit"), try: lt("previewTry") } } }),
              }}
            />
          )}
        />
      ) : (
        <div className="designer-work">
          <Outline t={t} state={state} dispatch={dispatch} newId={newId} />
          <Canvas t={t} typeNames={typeNames} state={state} dispatch={dispatch} newId={newId} grid={grid} onGridChange={setGrid} />
          <Inspector t={t} typeNames={typeNames} state={state} dispatch={dispatch} locale={locale} />
        </div>
      )}
      </Images.Provider>
      {previewing && !state.settings && <PreviewPanel t={t} surveyId={draft.surveyId} onClose={() => setPreviewing(false)} approval={approvalSlot} />}
      {savingTemplate && (
        <SaveTemplateDialog
          t={t}
          initialName={draft.title}
          onCancel={() => setSavingTemplate(false)}
          onSave={async (name) => {
            try {
              await api.saveAsTemplate(draft.surveyId, name);
              setNotice(t("savedAsTemplate"));
            } catch {
              setNotice(t("templateNotSaved"));
            }

            setSavingTemplate(false);
          }}
        />
      )}
      {notice !== null && (
        <p className="designer-notice" role="status" onAnimationEnd={() => setNotice(null)}>
          {notice}
        </p>
      )}
      {palette && <CommandPalette t={t} commands={commands()} onClose={() => setPalette(false)} />}
      {publishing && (
        <PublishDialog
          t={t}
          api={api}
          draft={draft}
          definition={state.definition}
          writeClipboard={writeClipboard}
          saveFile={saveFile}
          language={state.language}
          participants={participants}
          previewInvitation={previewInvitation === undefined ? undefined : (language) => previewInvitation(draft.surveyId, language)}
          locale={locale}
          approval={approvals === null ? undefined : approvalLine(w, approvals)}
          onFixTranslations={(language) => {
            setPublishing(false);
            dispatch({ type: "setLanguage", language });
          }}
          onClose={() => {
            setPublishing(false);
            setBaseline(state.definition);
            loadSummary();
          }}
        />
      )}
      {status.kind === "failed" && (
        <div className="designer-toast" role="alert">
          <h2>{t("saveFailedTitle")}</h2>
          <button type="button" className="designer-button" onClick={retryNow}>
            {t("retryNow")}
          </button>
          <p>{t("saveFailedText")}</p>
          {status.traceId !== undefined && <code>{t("saveReference", { traceId: status.traceId })}</code>}
        </div>
      )}
    </div>
  );
}

/** Save as template (AWK-17, W10 C): a name, prefilled with the survey's title. */
function SaveTemplateDialog({ t, initialName, onCancel, onSave }: { t: (key: DesignerMessageKey) => string; initialName: string; onCancel: () => void; onSave: (name: string) => Promise<void> }) {
  const [name, setName] = useState(initialName);
  const [busy, setBusy] = useState(false);
  const id = useId();

  return (
    <Dialog title={t("saveAsTemplate")} onClose={onCancel}>
      <form
        className="designer-dialog__form"
        onSubmit={(event) => {
          event.preventDefault();

          if (name.trim() !== "") {
            setBusy(true);
            void onSave(name.trim());
          }
        }}
      >
        <label className="aw-label" htmlFor={`${id}-name`}>
          {t("templateName")}
        </label>
        <input id={`${id}-name`} className="designer-input" autoFocus value={name} onChange={(event) => setName(event.target.value)} />
        <DialogActions>
          <button type="button" className="designer-button designer-button--quiet" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="designer-button designer-button--primary" disabled={busy || name.trim() === ""}>
            {t("save")}
          </button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** The editor's status: the save state while saving, and the survey's status once saved (W17). */
function barStatus(t: Translate<DesignerMessageKey>, w: WorkspaceText, status: SaveStatus, summary: SurveySummary | null) {
  // A template (W32) has no versions: only whether it is saved.
  if (summary?.templateId != null && status.kind !== "failed") {
    return status.kind === "pending" ? t("templateSaving") : t("templateSaved");
  }

  switch (status.kind) {
    case "pending":
      return t("statusPending");
    case "failed":
      return t("statusFailed", { seconds: status.retryInSeconds });
    case "saved":
      return summary === null || summary.publishedVersion === null ? t("statusSaved") : summaryText(w, summary);
  }
}

/** Saves a file through a temporary link, as a download. */
function downloadFile(name: string, file: Blob) {
  const url = URL.createObjectURL(file);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
