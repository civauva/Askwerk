// SPDX-License-Identifier: AGPL-3.0-only
import "./workspace.css";
import type { SurveysClient, SurveySummary } from "@askwerk/app-client";
import { createTranslator, type Translate } from "@askwerk/schema";
import { Dialog, DialogActions } from "@askwerk/ui";
import { useId, useState, type ReactNode } from "react";
import { Logo } from "../designer/Logo";
import { PreviewMenu } from "../designer/Preview";
import { useShellTemplate } from "../shell/AppShell";
import { workspaceMessages, type WorkspaceMessageKey } from "./messages";

export type WorkspaceText = Translate<WorkspaceMessageKey>;

/** The texts of the survey workspace in a language. */
export const workspaceText = (locale: "de" | "en"): WorkspaceText => createTranslator(workspaceMessages, locale);

/**
 * The survey's places (W17, W23): Create (Questions = build, Look = settings), Collect (Share, Participants),
 * Analyse (Results), and the survey's own settings page, which is opened from the bar, not a tab.
 */
export type SurveyTab = "build" | "settings" | "share" | "results" | "participants" | "survey-settings";

export type PrimaryAction = "publish" | "publishChanges" | "share";

/** The bar's one primary action: publish a draft, publish changes, or share what is published; none for readers. */
export function primaryAction(summary: SurveySummary | null, canEdit: boolean): PrimaryAction | null {
  if (!canEdit || summary === null || summary.status === "archived") {
    return null;
  }

  if (summary.publishedVersion === null) {
    return "publish";
  }

  return summary.hasChanges ? "publishChanges" : "share";
}

/** The status the bar shows when no screen gives its own. */
export function statusText(t: WorkspaceText, summary: SurveySummary): string {
  const closesAt = summary.closesAt ?? null;
  const passed = closesAt !== null && Date.parse(closesAt) <= Date.now();

  // A closing date (W24): when it will close, or the day it closed by it.
  if (summary.status === "published" && closesAt !== null && !passed) {
    return t("statusCloses", { status: statusOf(t, summary), date: dateText(t, closesAt, summary.timeZone, { day: "numeric", month: "short" }, true) });
  }

  if (summary.status === "closed" && closesAt !== null && passed) {
    return t("statusClosedOn", { date: dateText(t, closesAt, summary.timeZone, { day: "numeric", month: "short" }, false) });
  }

  return statusOf(t, summary);
}

/** A moment as the clock showed it in the time zone, in the screen's language: "1 Jul 2026" and, with the time, ", 18:00". */
export function dateText(t: WorkspaceText, at: string, timeZone: string | undefined, date: Intl.DateTimeFormatOptions, withTime: boolean): string {
  const moment = new Date(at);
  const day = new Intl.DateTimeFormat(t("dateLocale"), { ...date, timeZone }).format(moment);

  return withTime ? `${day}, ${new Intl.DateTimeFormat(t("dateLocale"), { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone }).format(moment)}` : day;
}

function statusOf(t: WorkspaceText, summary: SurveySummary): string {
  switch (summary.status) {
    case "published":
      return summary.hasChanges ? t("statusChanged", { version: summary.publishedVersion ?? 0 }) : t("statusPublished", { version: summary.publishedVersion ?? 0 });
    case "closed":
      return t("statusClosed");
    case "archived":
      return t("statusArchived");
    default:
      return t("statusDraft");
  }
}

/** What the survey's settings do on the server. */
export type SurveyActions = Pick<SurveysClient, "rename" | "duplicate" | "close" | "reopen" | "archive"> & Partial<Pick<SurveysClient, "saveAsTemplate" | "create">>;

type SurveyBarProps = {
  t: WorkspaceText;
  surveys: SurveyActions;
  surveyId: string;
  summary: SurveySummary | null;
  canEdit: boolean;
  /** Replaces the status, e.g. the editor's save state. */
  status?: ReactNode;
  languages?: readonly string[];
  language?: string;
  onLanguage?: (language: string) => void;
  /** Shown after the language switch, e.g. the missing translations. */
  languageNote?: ReactNode;
  /** The command palette; the search shows when given. */
  onSearch?: () => void;
  onPreview: (where: "panel" | "tab" | "pdf") => void;
  onPrimary: (action: PrimaryAction) => void;
  /** False hides Share as the primary action, on the Share tab itself. */
  showShare?: boolean;
  /** A screen's own action, before the primary one. */
  actions?: ReactNode;
  /** The approval next to the primary action (AWK-28, W16 C). */
  approval?: ReactNode;
  onBack: () => void;
  /** After the name changed. */
  onChanged: () => void;
  /** The profile menu at the far right, as on every other page (W23). */
  profile?: ReactNode;
  /** Inside the app's sidebar (AWK-38, W29, W31): a breadcrumb, the survey's name then the place; the way back is in the sidebar. */
  place?: string;
  /** The breadcrumb's survey name opens the survey's home, Questions (W31). */
  onHome?: () => void;
  /** A template being edited (W32): back to Templates, and "Use for a new survey" opens the new one. */
  onTemplates?: () => void;
  onOpenSurvey?: (surveyId: string) => void;
};

/**
 * One bar on every screen of a survey (AWK-29, W17, W23, W25): back to the surveys, the name (click to rename), the
 * status, search, language, Preview, one primary action and the profile. The survey's settings are a tab (W25).
 */
export function SurveyBar({
  t,
  surveys,
  surveyId,
  summary,
  canEdit,
  status,
  languages,
  language,
  onLanguage,
  languageNote,
  onSearch,
  onPreview,
  onPrimary,
  showShare = true,
  actions,
  approval,
  onBack,
  onChanged,
  profile,
  place,
  onHome,
  onTemplates,
  onOpenSurvey,
}: SurveyBarProps) {
  // A template (W32) is used, never published: its one action makes a new survey from it.
  const templateId = summary?.templateId ?? null;
  const primary = templateId === null ? primaryAction(summary, canEdit) : null;
  const action = primary === "share" && !showShare ? null : primary;
  const [using, setUsing] = useState(false);
  const useTemplate = () => {
    if (templateId === null || surveys.create === undefined) {
      return;
    }

    setUsing(true);
    surveys.create({ title: summary?.title ?? "", languages: [], defaultLanguage: "", templateId }).then(
      (created) => onOpenSurvey?.(created),
      () => setUsing(false),
    );
  };
  const templatesCrumb = templateId !== null && (
    <>
      <a
        href="/templates"
        className="survey-bar__crumb"
        onClick={(event) => {
          event.preventDefault();
          (onTemplates ?? onBack)();
        }}
      >
        {t("templates")}
      </a>
      <span className="survey-bar__crumb-sep" aria-hidden="true">
        ›
      </span>
    </>
  );

  return (
    <header className={place === undefined ? "survey-bar" : "survey-bar survey-bar--place"} aria-label={t("survey")}>
      {place !== undefined ? (
        <nav className="survey-bar__crumbs" aria-label={t("whereYouAre")}>
          {templatesCrumb}
          {/* W39: every path starts at its section, Surveys for a survey. */}
          {templateId === null && (
            <>
              <a
                href="/"
                className="survey-bar__crumb"
                onClick={(event) => {
                  event.preventDefault();
                  onBack();
                }}
              >
                {t("surveys")}
              </a>
              <span className="survey-bar__crumb-sep" aria-hidden="true">
                ›
              </span>
            </>
          )}
          {/* A template (W32): Templates › its name; the place is in the sidebar. */}
          {templateId !== null ? (
            <h1 className="survey-bar__place" aria-current="page" title={summary?.title ?? ""}>
              {summary?.title ?? ""}
            </h1>
          ) : (
          <>
          <a
            href="#build"
            className="survey-bar__crumb"
            title={summary?.title ?? ""}
            onClick={(event) => {
              event.preventDefault();
              onHome?.();
            }}
          >
            {summary?.title ?? ""}
          </a>
          <span className="survey-bar__crumb-sep" aria-hidden="true">
            ›
          </span>
          <h1 className="survey-bar__place" aria-current="page">
            {place}
          </h1>
          </>
          )}
        </nav>
      ) : (
      <>
      <a
        href={templateId === null ? "/" : "/templates"}
        className="survey-bar__back"
        onClick={(event) => {
          event.preventDefault();
          (templateId === null ? onBack : (onTemplates ?? onBack))();
        }}
      >
        <span aria-hidden="true">
          <Logo />
        </span>
        <span>{templateId === null ? t("surveys") : t("templates")}</span>
      </a>
      <span className="survey-bar__sep" aria-hidden="true">
        ›
      </span>
      <h1 className="survey-bar__title">
        <SurveyName t={t} surveys={surveys} surveyId={surveyId} title={summary?.title ?? ""} canEdit={canEdit} onChanged={onChanged} />
      </h1>
      </>
      )}
      {/* A template (W32): one chip, with the editor's save state when it gives one. */}
      {templateId !== null ? (
        <span className="survey-bar__template" title={t("templateHint")}>{status ?? t("templateChip")}</span>
      ) : (
        (status !== undefined || summary !== null) && <span className="survey-bar__status">{status ?? (summary === null ? null : statusText(t, summary))}</span>
      )}
      <span className="survey-bar__grow" />
      {templateId !== null && <span className="survey-bar__hint">{t("templateHint")}</span>}
      {onSearch !== undefined && (
        <button type="button" className="survey-bar__search" aria-label={t("search")} aria-keyshortcuts="Control+K" onClick={onSearch}>
          <span aria-hidden="true">{t("searchHint")}</span>
          <kbd aria-hidden="true">Ctrl K</kbd>
        </button>
      )}
      {languages !== undefined && languages.length > 1 && onLanguage !== undefined && (
        <span className="designer-segment" role="group" aria-label={t("language")}>
          {languages.map((each) => (
            <button key={each} type="button" aria-pressed={language === each} onClick={() => onLanguage(each)}>
              {each.toUpperCase()}
            </button>
          ))}
        </span>
      )}
      {languageNote}
      <PreviewMenu t={t} onSidePanel={() => onPreview("panel")} onNewTab={() => onPreview("tab")} onPdf={() => onPreview("pdf")} />
      {actions}
      {/* A template is never published, so it needs no approval (W32). */}
      {templateId === null && approval}
      {action !== null && (
        <button type="button" className="designer-button designer-button--primary survey-bar__primary" onClick={() => onPrimary(action)}>
          {t(action)}
        </button>
      )}
      {templateId !== null && canEdit && surveys.create !== undefined && (
        <button type="button" className="designer-button designer-button--primary survey-bar__primary" disabled={using} onClick={useTemplate}>
          {t("useTemplate")}
        </button>
      )}
      {profile !== undefined && (
        <>
          <span className="survey-bar__divider" aria-hidden="true" />
          {profile}
        </>
      )}
    </header>
  );
}

/** The name: click to rename it in place (W23); Enter or leaving the field saves, Escape keeps the old name. */
function SurveyName({ t, surveys, surveyId, title, canEdit, onChanged }: { t: WorkspaceText; surveys: SurveyActions; surveyId: string; title: string; canEdit: boolean; onChanged: () => void }) {
  const [editing, setEditing] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  if (!canEdit) {
    return <span className="survey-bar__name survey-bar__name--plain">{title}</span>;
  }

  if (editing === null) {
    return (
      <button type="button" className="survey-bar__name" title={t("renameHint")} aria-label={t("renameNamed", { title })} onClick={() => setEditing(title)}>
        {title}
      </button>
    );
  }

  const save = () => {
    const next = editing.trim();

    if (next === "" || next === title) {
      setEditing(null);
      return;
    }

    surveys.rename(surveyId, next).then(
      () => {
        setEditing(null);
        setFailed(false);
        onChanged();
      },
      () => setFailed(true),
    );
  };

  return (
    <input
      className="survey-bar__name-input"
      aria-label={t("titleLabel")}
      aria-invalid={failed}
      autoFocus
      maxLength={200}
      value={editing}
      size={Math.max(8, Math.min(40, editing.length + 1))}
      onChange={(event) => setEditing(event.target.value)}
      onBlur={save}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          save();
        } else if (event.key === "Escape") {
          setEditing(null);
          setFailed(false);
        }
      }}
    />
  );
}

function GearIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
      <circle cx="12" cy="12" r="3.2" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3 1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8 1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />
    </svg>
  );
}

/** A dialog asking for a title, e.g. for a duplicate or a template. */
export function TitleDialog({ t, heading, confirm, initial, onCancel, onConfirm }: { t: WorkspaceText; heading: string; confirm: string; initial: string; onCancel: () => void; onConfirm: (title: string) => Promise<unknown> }) {
  const id = useId();
  const [title, setTitle] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  return (
    <Dialog title={heading} onClose={onCancel}>
      <form
        className="designer-dialog__form"
        onSubmit={(event) => {
          event.preventDefault();
          setBusy(true);
          setFailed(false);
          onConfirm(title.trim()).catch(() => {
            setBusy(false);
            setFailed(true);
          });
        }}
      >
        <label className="aw-label" htmlFor={`${id}-name`}>
          {t("titleLabel")}
        </label>
        <input id={`${id}-name`} className="designer-input" autoFocus maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} />
        {failed && <p className="designer-error">{t("failed")}</p>}
        <DialogActions>
          <button type="button" className="designer-button designer-button--quiet" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="designer-button designer-button--primary" disabled={busy || title.trim() === ""}>
            {confirm}
          </button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** The places in their groups (W23): Create, Collect, Analyse. Readers see Collect and Analyse only. */
const groups: readonly { label: "groupCreate" | "groupCollect" | "groupAnalyse"; tabs: readonly SurveyTab[]; edit?: boolean }[] = [
  { label: "groupCreate", tabs: ["build", "settings"], edit: true },
  { label: "groupCollect", tabs: ["share", "participants"] },
  { label: "groupAnalyse", tabs: ["results"] },
];

/** One strong icon at the start of each group's tile (W25 C): a pencil, a tray, a chart. */
function GroupIcon({ group }: { group: (typeof groups)[number]["label"] }) {
  const paths =
    group === "groupCreate" ? (
      <>
        <path d="M4 20h4L19 9l-4-4L4 16v4z" />
        <path d="M13.5 6.5l4 4" />
      </>
    ) : group === "groupCollect" ? (
      <>
        <path d="M4 13l2.5-7h11L20 13" />
        <path d="M4 13v6h16v-6h-5l-1.5 2.5h-3L9 13H4z" />
      </>
    ) : (
      <>
        <path d="M4 20V4" />
        <path d="M4 20h16" />
        <path d="M8 16v-4" />
        <path d="M12 16V8" />
        <path d="M16 16v-6" />
      </>
    );

  return (
    <span className="survey-tabs__icon" aria-hidden="true">
      <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
        {paths}
      </svg>
    </span>
  );
}

/**
 * The survey's places as tabs (W17, W23, W25 C): each group on a soft tile with one strong icon at its start, its name
 * for screen readers; the survey's settings at the right end, for everyone (read-only for readers).
 */
export function SurveyTabs({ t, active, canEdit, summary, onSelect }: { t: WorkspaceText; active: SurveyTab; canEdit: boolean; summary: SurveySummary | null; onSelect: (tab: SurveyTab) => void }) {
  const count = (tab: SurveyTab) => (tab === "results" ? summary?.responses : tab === "participants" ? summary?.participants : undefined);
  const shown = groups
    .filter((group) => (canEdit || group.edit !== true) && (summary?.templateId == null || group.edit === true))
    .map((group) => ({ ...group, tabs: group.tabs.filter((tab) => canEdit || tab !== "share") }))
    .filter((group) => group.tabs.length > 0);
  const link = (tab: SurveyTab, children: ReactNode, label?: string) => (
    <a
      key={tab}
      href={`#${tab}`}
      className={`survey-tabs__tab${tab === "survey-settings" ? " survey-tabs__tab--settings" : ""}`}
      aria-label={label}
      aria-current={tab === active ? "page" : undefined}
      onClick={(event) => {
        event.preventDefault();
        onSelect(tab);
      }}
    >
      {children}
    </a>
  );

  return (
    <nav className="survey-tabs" aria-label={t("survey")}>
      {shown.map((group) => (
        <div key={group.label} className="survey-tabs__group" role="group" aria-label={t(group.label)}>
          <GroupIcon group={group.label} />
          {group.tabs.map((tab) =>
            link(
              tab,
              <>
                {t(tab)}
                {count(tab) !== undefined && <span className="survey-tabs__count">{count(tab)}</span>}
              </>,
              count(tab) === undefined ? undefined : `${t(tab)} ${count(tab)}`,
            ),
          )}
        </div>
      ))}
      <span className="survey-tabs__grow" />
      <span className="survey-tabs__divider" aria-hidden="true" />
      {link(
        "survey-settings",
        <>
          <GearIcon />
          <span>{t("settingsTab")}</span>
        </>,
        t("surveySettings"),
      )}
    </nav>
  );
}

/**
 * The survey's places in the app's sidebar (AWK-38, W29 drill-in; W31: the name is in the breadcrumb): Create · Collect ·
 * Analyse and Survey settings. The same links, names and counts as SurveyTabs, so both read the same.
 */
export function SurveyPlaces({ t, active, canEdit, summary, onSelect }: { t: WorkspaceText; active: SurveyTab; canEdit: boolean; summary: SurveySummary | null; onSelect: (tab: SurveyTab) => void }) {
  // A template's way back is to Templates (W32).
  useShellTemplate(summary?.templateId != null);
  const count = (tab: SurveyTab) => (tab === "results" ? summary?.responses : tab === "participants" ? summary?.participants : undefined);
  const shown = groups
    .filter((group) => (canEdit || group.edit !== true) && (summary?.templateId == null || group.edit === true))
    .map((group) => ({ ...group, tabs: group.tabs.filter((tab) => canEdit || tab !== "share") }))
    .filter((group) => group.tabs.length > 0);
  const link = (tab: SurveyTab, children: ReactNode, label?: string) => (
    <a
      key={tab}
      href={`#${tab}`}
      className="shell-item"
      aria-label={label}
      aria-current={tab === active ? "page" : undefined}
      onClick={(event) => {
        event.preventDefault();
        onSelect(tab);
      }}
    >
      {children}
    </a>
  );

  return (
    <>
      <nav className="survey-places" aria-label={t("survey")}>
        {shown.map((group) => (
          <div key={group.label} role="group" aria-label={t(group.label)}>
            <div className="shell-group" aria-hidden="true">
              {t(group.label)}
            </div>
            {group.tabs.map((tab) =>
              link(
                tab,
                <>
                  <span>{t(tab)}</span>
                  {count(tab) !== undefined && <span className="shell-count">{count(tab)}</span>}
                </>,
                count(tab) === undefined ? undefined : `${t(tab)} ${count(tab)}`,
              ),
            )}
          </div>
        ))}
        <div className="survey-places__end">
          {link(
            "survey-settings",
            <>
              <GearIcon />
              <span>{t("settingsTab")}</span>
            </>,
            t("surveySettings"),
          )}
        </div>
      </nav>
    </>
  );
}
