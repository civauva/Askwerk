// SPDX-License-Identifier: AGPL-3.0-only
import type { ParticipantsClient, SurveysClient, SurveySummary } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useShellSlot } from "../shell/AppShell";
import { designerMessages } from "../designer/messages";
import { pdfUrl, PreviewPanel, previewUrl } from "../designer/Preview";
import { Published } from "../designer/Published";
import { ParticipantsPanel } from "../participants/ParticipantsPanel";
import { ApprovalPill, ApprovalSlot, useApprovals, type ApprovalClient } from "./Approvals";
import { SurveyBar, SurveyPlaces, SurveyTabs, workspaceText, type SurveyActions, type SurveyTab, type WorkspaceText } from "./SurveyBar";

/** The survey endpoints the workspace's screens use. */
export type WorkspaceSurveys = SurveyActions & ApprovalClient & Pick<SurveysClient, "summary" | "links" | "setClosingDate" | "setOneResponse">;

/** How a screen of a survey leads to the others. */
export type WorkspaceNavigation = {
  surveys: WorkspaceSurveys;
  surveyId: string;
  locale: Locale;
  canEdit: boolean;
  onTab: (tab: SurveyTab) => void;
  onBack: () => void;
  onOpenSurvey?: (surveyId: string) => void;
  openWindow?: (url: string) => void;
  /** The profile menu at the bar's far right (W23). */
  profile?: ReactNode;
};

type SurveyFrameProps = WorkspaceNavigation & {
  active: SurveyTab;
  languages?: readonly string[];
  language?: string;
  onLanguage?: (language: string) => void;
  /** A screen's own action, before the primary one, e.g. Export. */
  actions?: ReactNode;
  className?: string;
  children: (frame: { t: WorkspaceText; summary: SurveySummary | null; reload: () => void }) => ReactNode;
};

/** The survey's summary, loaded again after the menu changed the survey. */
export function useSurveySummary(surveys: WorkspaceSurveys, surveyId: string) {
  const [summary, setSummary] = useState<SurveySummary | null>(null);
  const reload = useCallback(() => {
    surveys.summary(surveyId).then(setSummary, () => undefined);
  }, [surveys, surveyId]);

  useEffect(reload, [reload]);

  return [summary, reload] as const;
}

/** A screen of a survey other than the editor (W17): the bar, the tabs and the screen. */
export function SurveyFrame({
  surveys,
  surveyId,
  locale,
  canEdit,
  onTab,
  onBack,
  onOpenSurvey,
  openWindow = (url) => void window.open(url, "_blank", "noopener"),
  profile,
  active,
  languages,
  language,
  onLanguage,
  actions,
  className,
  children,
}: SurveyFrameProps) {
  const t = useMemo(() => workspaceText(locale), [locale]);
  const [summary, reload] = useSurveySummary(surveys, surveyId);
  const [previewing, setPreviewing] = useState(false);
  const [approvals, reloadApprovals] = useApprovals(surveys, surveyId);
  const pdf = pdfUrl(surveyId, language ?? summary?.languages[0] ?? locale);
  // Inside the app's sidebar (AWK-38, W29) the survey's places go there and the bar names the place.
  const slot = useShellSlot();
  const select = (tab: SurveyTab) => tab !== active && onTab(tab);

  return (
    <div className={`survey-frame${className === undefined ? "" : ` ${className}`}`} lang={locale}>
      <SurveyBar
        t={t}
        surveys={surveys}
        surveyId={surveyId}
        summary={summary}
        canEdit={canEdit}
        {...(languages === undefined ? {} : { languages })}
        {...(language === undefined ? {} : { language })}
        {...(onLanguage === undefined ? {} : { onLanguage })}
        onPreview={(where) => (where === "panel" ? setPreviewing(true) : openWindow(where === "pdf" ? pdf : previewUrl(surveyId)))}
        onPrimary={(action) => onTab(action === "share" ? "share" : "build")}
        showShare={active !== "share"}
        actions={actions}
        approval={canEdit && approvals !== null && <ApprovalPill t={t} approvals={approvals} />}
        onBack={onBack}
        onChanged={reload}
        {...(slot === null ? { profile } : { place: t(active) })}
      />
      {slot === null ? <SurveyTabs t={t} active={active} canEdit={canEdit} summary={summary} onSelect={select} /> : createPortal(<SurveyPlaces t={t} active={active} canEdit={canEdit} summary={summary} onSelect={select} />, slot)}
      {children({ t, summary, reload })}
      {previewing && (
        <PreviewPanel
          t={t}
          surveyId={surveyId}
          onClose={() => setPreviewing(false)}
          approval={
            <ApprovalSlot
              t={t}
              approvals={approvals}
              title={summary?.title ?? ""}
              languages={summary?.languages ?? []}
              onApprove={(note) => surveys.approve(surveyId, note).then(reloadApprovals)}
              onWithdraw={(approvalId) => surveys.withdrawApproval(surveyId, approvalId).then(reloadApprovals)}
            />
          }
        />
      )}
    </div>
  );
}

type ShareProps = WorkspaceNavigation & {
  participants?: ParticipantsClient;
  preview?: (language: string) => Promise<{ subject: string; text: string }>;
  writeClipboard?: (text: string) => Promise<void>;
  saveFile: (name: string, file: Blob) => void;
  /** The survey's own websites for its frames (AWK-13). */
  embedSites?: (surveyId: string) => NonNullable<Parameters<typeof Published>[0]["embedSites"]>;
};

/** The Share tab (W17 C): the published survey's link, QR code, embed code, PDF and invitations. */
export function SharePage({ participants, preview, writeClipboard = (text) => navigator.clipboard.writeText(text), saveFile, embedSites, ...navigation }: ShareProps) {
  const { surveys, surveyId, locale } = navigation;
  const designerText = useMemo(() => createTranslator(designerMessages, locale), [locale]);
  const [links, setLinks] = useState<Awaited<ReturnType<WorkspaceSurveys["links"]>> | null>(null);
  const [language, setLanguage] = useState<string | null>(null);
  const sites = useMemo(() => embedSites?.(surveyId), [embedSites, surveyId]);

  useEffect(() => {
    surveys.links(surveyId).then(setLinks, () => setLinks([]));
  }, [surveys, surveyId]);

  return (
    <SurveyFrame {...navigation} active="share">
      {({ t, summary }) => {
        if (summary === null) {
          return null;
        }

        const shown = language !== null && summary.languages.includes(language) ? language : summary.languages.includes(locale) ? locale : (summary.languages[0] ?? locale);

        return (
          <main className="survey-page">
            <h2 className="survey-page__title">{t("share")}</h2>
            {summary.publishedVersion === null ? (
              <p className="survey-page__empty">{t("shareFirst")}</p>
            ) : (
              <>
                {summary.languages.length > 1 && (
                  <span className="designer-segment" role="group" aria-label={t("language")}>
                    {summary.languages.map((each) => (
                      <button key={each} type="button" aria-pressed={shown === each} onClick={() => setLanguage(each)}>
                        {each.toUpperCase()}
                      </button>
                    ))}
                  </span>
                )}
                {links !== null && (
                  <Published
                    t={designerText}
                    surveyId={surveyId}
                    title={summary.title}
                    language={shown}
                    links={links}
                    writeClipboard={writeClipboard}
                    saveFile={saveFile}
                    participants={participants}
                    preview={preview}
                    locale={locale}
                    embedSites={sites}
                  />
                )}
              </>
            )}
          </main>
        );
      }}
    </SurveyFrame>
  );
}

type ParticipantsPageProps = WorkspaceNavigation & {
  participants: ParticipantsClient;
  preview?: (language: string) => Promise<{ subject: string; text: string }>;
  timeZone?: string;
};

/** The Participants tab (W17 E): who was invited and what happened. */
export function ParticipantsPage({ participants, preview, timeZone, ...navigation }: ParticipantsPageProps) {
  return (
    <SurveyFrame {...navigation} active="participants">
      {() => (
        <main className="survey-page survey-page--wide">
          <ParticipantsPanel client={participants} surveyId={navigation.surveyId} locale={navigation.locale} {...(preview === undefined ? {} : { preview })} {...(timeZone === undefined ? {} : { timeZone })} />
        </main>
      )}
    </SurveyFrame>
  );
}
