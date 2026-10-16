// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type SurveySummary } from "@askwerk/app-client";
import { ConfirmDialog, Field, Switch } from "@askwerk/ui";
import { useState } from "react";
import { SurveyFrame, type WorkspaceNavigation } from "./SurveyFrame";
import { dateText, TitleDialog, type WorkspaceText } from "./SurveyBar";

type Dialog = "duplicate" | "template" | "close" | "archive" | null;

/**
 * ⚙ Survey settings (W23): the survey's real settings in one place, labelled as such. Its name and languages, whether
 * it collects responses (with a closing date and one response per person, W24), and the rare actions at the foot:
 * duplicate, save as template, archive. Readers see it read-only.
 */
export function SurveySettingsPage(navigation: WorkspaceNavigation) {
  return (
    <SurveyFrame {...navigation} active="survey-settings">
      {({ t, summary, reload }) => (summary === null ? null : <Settings t={t} summary={summary} navigation={navigation} onChanged={reload} />)}
    </SurveyFrame>
  );
}

function Settings({ t, summary, navigation, onChanged }: { t: WorkspaceText; summary: SurveySummary; navigation: WorkspaceNavigation; onChanged: () => void }) {
  const { surveys, surveyId, canEdit, onBack, onOpenSurvey } = navigation;
  const [name, setName] = useState(summary.title);
  const [saved, setSaved] = useState(false);
  const [failed, setFailed] = useState(false);
  const [dialog, setDialog] = useState<Dialog>(null);

  const run = (work: Promise<unknown>, then: () => void) =>
    work.then(
      () => {
        setDialog(null);
        then();
      },
      () => setFailed(true),
    );

  const saveName = () => {
    setSaved(false);
    setFailed(false);
    void surveys.rename(surveyId, name.trim()).then(
      () => {
        setSaved(true);
        onChanged();
      },
      () => setFailed(true),
    );
  };

  return (
    <main className="survey-page survey-settings">
      <h2 className="survey-page__title">{t("surveySettings")}</h2>

      <section className="survey-settings__section" aria-labelledby="survey-settings-general">
        <h3 id="survey-settings-general">{t("settingsGeneral")}</h3>
        {canEdit ? (
          <div className="survey-settings__name">
            <Field label={t("titleLabel")}>
              <input className="designer-input" maxLength={200} value={name} onChange={(event) => (setName(event.target.value), setSaved(false))} />
            </Field>
            <button type="button" className="designer-button designer-button--quiet" disabled={name.trim() === "" || name.trim() === summary.title} onClick={saveName}>
              {t("save")}
            </button>
            {saved && <span role="status">{t("saved")}</span>}
          </div>
        ) : (
          <p>
            <span className="aw-label">{t("titleLabel")}</span> {summary.title}
          </p>
        )}
        <p className="survey-settings__line">
          <span className="aw-label">{t("languages")}</span> {summary.languages.map((language) => language.toUpperCase()).join(" · ")}
        </p>
        <p className="designer-note">{t("languagesNote")}</p>
      </section>

      <Collecting t={t} summary={summary} navigation={navigation} onChanged={onChanged} onClose={() => setDialog("close")} onFailed={() => setFailed(true)} />

      {canEdit && (
        <section className="survey-settings__section" aria-labelledby="survey-settings-more">
          <h3 id="survey-settings-more">{t("settingsMore")}</h3>
          <div className="survey-settings__actions">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => setDialog("duplicate")}>
              {t("duplicate")}
            </button>
            {surveys.saveAsTemplate !== undefined && (
              <button type="button" className="designer-button designer-button--quiet" onClick={() => setDialog("template")}>
                {t("saveAsTemplate")}
              </button>
            )}
            <button type="button" className="designer-button designer-button--quiet survey-settings__danger" onClick={() => setDialog("archive")}>
              {t("archive")}
            </button>
          </div>
        </section>
      )}

      {failed && (
        <p className="designer-error" role="alert">
          {t("failed")}
        </p>
      )}

      {dialog === "duplicate" && (
        <TitleDialog
          t={t}
          heading={t("duplicate")}
          confirm={t("duplicate")}
          initial={t("copyOf", { title: summary.title })}
          onCancel={() => setDialog(null)}
          onConfirm={(title) => surveys.duplicate(surveyId, title).then((copy) => (setDialog(null), onOpenSurvey?.(copy)))}
        />
      )}
      {dialog === "template" && surveys.saveAsTemplate !== undefined && (
        <TitleDialog
          t={t}
          heading={t("saveAsTemplate")}
          confirm={t("save")}
          initial={summary.title}
          onCancel={() => setDialog(null)}
          onConfirm={(title) => surveys.saveAsTemplate!(surveyId, title).then(() => setDialog(null))}
        />
      )}
      {dialog === "close" && (
        <ConfirmDialog
          title={t("closeForResponsesTitle")}
          text={t("closeAsk", { title: summary.title })}
          confirm={t("closeForResponsesTitle")}
          cancel={t("cancel")}
          onCancel={() => setDialog(null)}
          onConfirm={() => run(surveys.close(surveyId), onChanged)}
        />
      )}
      {dialog === "archive" && (
        <ConfirmDialog
          title={t("archiveTitle")}
          text={t("archiveAsk", { title: summary.title })}
          confirm={t("archiveTitle")}
          cancel={t("cancel")}
          onCancel={() => setDialog(null)}
          onConfirm={() => run(surveys.archive(surveyId), onBack)}
        />
      )}
    </main>
  );
}

/** The date-time field's value for a moment, as the clock showed it in the time zone: 2026-10-31T18:00. */
function localValue(at: string, timeZone: string): string {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone })
      .formatToParts(new Date(at))
      .map((part) => [part.type, part.value]),
  );

  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
}

type CollectingProps = { t: WorkspaceText; summary: SurveySummary; navigation: WorkspaceNavigation; onChanged: () => void; onClose: () => void; onFailed: () => void };

/**
 * Collecting responses (W23, W24): whether the link takes responses, the closing date in the instance's time zone
 * (none by default), one response per person (on by default), and closing or reopening by hand.
 */
function Collecting({ t, summary, navigation, onChanged, onClose, onFailed }: CollectingProps) {
  const { surveys, surveyId, canEdit } = navigation;
  const zone = summary.timeZone ?? "UTC";
  const closesAt = summary.closesAt ?? null;
  const passed = closesAt !== null && Date.parse(closesAt) <= Date.now();
  const [editing, setEditing] = useState<string | null>(null);
  const [dateError, setDateError] = useState<string | null>(null);
  const [one, setOne] = useState(summary.oneResponsePerPerson !== false);
  const [oneSaved, setOneSaved] = useState(false);
  const long = (at: string) => dateText(t, at, zone, { day: "numeric", month: "long", year: "numeric" }, true);
  const collectingDates = summary.status === "draft" || summary.status === "published";

  const saveDate = (value: string | null) => {
    setDateError(null);
    surveys.setClosingDate(surveyId, value).then(
      () => {
        setEditing(null);
        onChanged();
      },
      (error: unknown) => {
        const code = error instanceof ApiError ? error.problem.errors?.["closesAt"]?.[0] : undefined;

        if (code === undefined) {
          onFailed();
        } else {
          setDateError(code === "closing-date-past" ? t("closingDatePast") : t("failed"));
        }
      },
    );
  };

  const toggleOne = (on: boolean) => {
    setOne(on);
    setOneSaved(false);
    surveys.setOneResponse(surveyId, on).then(
      () => {
        setOneSaved(true);
        onChanged();
      },
      () => {
        setOne(!on);
        onFailed();
      },
    );
  };

  const status =
    summary.status === "closed"
      ? closesAt !== null && passed
        ? t("collectingClosedOn", { date: long(closesAt) })
        : t("collectingClosed")
      : summary.publishedVersion === null
        ? t("collectingDraft")
        : closesAt !== null
          ? t("collectingUntil", { date: long(closesAt) })
          : t("collectingOpen");

  return (
    <section className="survey-settings__section" aria-labelledby="survey-settings-collecting">
      <h3 id="survey-settings-collecting">{t("settingsCollecting")}</h3>
      <p>{status}</p>

      {collectingDates && editing !== null && (
        <div className="survey-settings__date">
          <div className="survey-settings__name">
            <Field label={t("closingDate")}>
              <input type="datetime-local" className="designer-input" value={editing} onChange={(event) => setEditing(event.target.value)} />
            </Field>
            <button type="button" className="designer-button" aria-label={t("closingDateSave")} disabled={editing === ""} onClick={() => saveDate(editing)}>
              {t("save")}
            </button>
            <button type="button" className="designer-button designer-button--quiet" onClick={() => (setEditing(null), setDateError(null))}>
              {t("cancel")}
            </button>
          </div>
          <p className="designer-note">{t("closingDateNote", { zone })}</p>
          {dateError !== null && (
            <p className="designer-error" role="alert">
              {dateError}
            </p>
          )}
        </div>
      )}
      {collectingDates && editing === null && (
        <p className="survey-settings__line">
          <span className="aw-label">{t("closingDate")}</span>{" "}
          <span className="survey-settings__value">
            <span>{closesAt === null ? t("closingDateNone") : dateText(t, closesAt, zone, { day: "numeric", month: "short", year: "numeric" }, true)}</span>
            {canEdit && closesAt === null && (
              <button type="button" className="designer-button designer-button--quiet" onClick={() => setEditing("")}>
                {t("closingDateSet")}
              </button>
            )}
            {canEdit && closesAt !== null && (
              <>
                <button type="button" className="designer-button designer-button--quiet" onClick={() => setEditing(localValue(closesAt, zone))}>
                  {t("closingDateChange")}
                </button>
                <button type="button" className="designer-button designer-button--quiet" onClick={() => saveDate(null)}>
                  {t("closingDateRemove")}
                </button>
              </>
            )}
          </span>
        </p>
      )}

      {summary.status !== "closed" && (
        <div className="survey-settings__line">
          {canEdit ? (
            <span className="survey-settings__value">
              <Switch label={t("oneResponse")} checked={one} onChange={toggleOne} />
              {oneSaved && <span role="status">{t("saved")}</span>}
            </span>
          ) : (
            <p>
              <span className="aw-label">{t("oneResponse")}</span> <span>{one ? t("on") : t("off")}</span>
            </p>
          )}
          <p className="designer-note">{t("oneResponseNote")}</p>
        </div>
      )}

      {canEdit && summary.status === "published" && (
        <button type="button" className="designer-button designer-button--quiet" onClick={onClose}>
          {t("closeForResponses")}
        </button>
      )}
      {canEdit && summary.status === "closed" && (
        <>
          <button type="button" className="designer-button designer-button--quiet" onClick={() => void surveys.reopen(surveyId).then(onChanged, onFailed)}>
            {t("reopen")}
          </button>
          {closesAt !== null && passed && <p className="designer-note">{t("reopenRemovesDate")}</p>}
        </>
      )}
    </section>
  );
}
