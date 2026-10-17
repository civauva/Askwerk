// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type ParticipantDetail, type ParticipantEvent, type ParticipantsClient, type ParticipantStatus } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Dialog } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { dayTimeText, fieldLabel, shortLink } from "./peopleFormat";
import { peopleMessages, type PeopleMessageKey } from "./peopleMessages";

const historyKeys: Record<ParticipantEvent["kind"], PeopleMessageKey> = {
  invited: "historyInvited",
  sent: "historySent",
  reminded: "historyReminded",
  opened: "historyOpened",
  started: "historyStarted",
  answered: "historyAnswered",
  bounced: "historyBounced",
  unsubscribed: "historyUnsubscribed",
};

type PersonPanelProps = {
  client: ParticipantsClient;
  surveyId: string;
  participantId: string;
  locale: Locale;
  timeZone?: string;
  /** The status the list shows, for its colour. */
  status?: ParticipantStatus;
  /** The status the list shows, e.g. "Opened, not started". */
  statusText: string;
  writeClipboard: (text: string) => Promise<void>;
  onClose: (changed: boolean) => void;
  onRemove: () => void;
};

/** What happened, in words (W35): where they came from, and whether a reminder was automatic. */
function eventText(t: ReturnType<typeof createTranslator<PeopleMessageKey>>, entry: ParticipantEvent): string {
  if (entry.kind === "invited" && entry.source != null) {
    if (entry.source === "contacts") {
      return t("historyInvitedContacts");
    }

    if (entry.source.startsWith("import:")) {
      const file = entry.source.slice("import:".length);
      return file === "" ? t("historyInvitedPasted") : t("historyInvitedImport", { file });
    }
  }

  if (entry.kind === "reminded") {
    const count = entry.count ?? 1;
    return entry.automatic === true ? t("historyRemindedAuto", { count }) : entry.automatic === false ? t("historyRemindedManual", { count }) : t("historyReminded", { count });
  }

  return t(historyKeys[entry.kind]);
}

/**
 * One person (W35): email, name, language, fields and External ID to change; the personal link; what happened and the
 * next automatic reminder; a reminder now, no more reminders, remove, save.
 */
export function PersonPanel({ client, surveyId, participantId, locale, timeZone, status, statusText, writeClipboard, onClose, onRemove }: PersonPanelProps) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const id = useId();
  const [person, setPerson] = useState<ParticipantDetail | null>(null);
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [language, setLanguage] = useState("");
  const [externalId, setExternalId] = useState("");
  const [fields, setFields] = useState<[string, string][]>([]);
  const [errors, setErrors] = useState<Partial<Record<"email" | "externalId", PeopleMessageKey>>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [changed, setChanged] = useState(false);
  const when = (at: string) => dayTimeText(locale, at, timeZone);

  const load = () =>
    client.detail(surveyId, participantId).then((detail) => {
      setPerson(detail);
      setEmail(detail.email);
      setName(detail.name ?? "");
      setLanguage(detail.language ?? "");
      setExternalId(detail.externalId ?? "");
      setFields(Object.entries(detail.fields));
    });

  useEffect(() => {
    void load();
    // load depends on the ids only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, surveyId, participantId]);

  const save = () => {
    setErrors({});
    setNotice(null);
    client
      .change(surveyId, participantId, { email, name: name || null, language: language || null, externalId: externalId || null, fields: Object.fromEntries(fields.filter(([key, value]) => key.trim() !== "" && value.trim() !== "")) })
      .then(
        () => {
          setChanged(true);
          setNotice(t("changed"));
          void load();
        },
        (caught: unknown) => {
          const problem = caught instanceof ApiError ? caught.problem.errors : undefined;
          const codes: Record<string, PeopleMessageKey> = { "email-invalid": "emailInvalid", "email-taken": "emailTaken", "external-id-taken": "externalIdTaken" };
          const next: typeof errors = {};
          problem?.email?.[0] !== undefined && (next.email = codes[problem.email[0]] ?? "failed");
          problem?.externalId?.[0] !== undefined && (next.externalId = codes[problem.externalId[0]] ?? "failed");
          setErrors(next);
          Object.keys(next).length === 0 && setNotice(t("failed"));
        },
      );
  };

  const remind = () =>
    client.remind(surveyId, { participantIds: [participantId] }).then(
      () => setNotice(t("reminded", { count: 1 })),
      (caught: unknown) => setNotice(caught instanceof ApiError && caught.problem.code === "email-not-set-up" ? t("noEmail") : t("failed")),
    );

  const toggleReminders = () =>
    person !== null &&
    client.setRemindersOff(surveyId, participantId, !person.remindersOff).then(() => {
      setChanged(true);
      void load();
    });

  const nextReminder = person === null ? 1 : Math.max(0, ...person.history.filter((entry) => entry.kind === "reminded").map((entry) => entry.count ?? 1)) + 1;

  return (
    <Dialog
      title={person?.name ?? person?.email ?? t("participant")}
      kicker={t("participant")}
      onClose={() => onClose(changed)}
      className="person-panel"
      head={
        <p className="person-panel__status" data-status={status}>
          <i className="participants__dot" aria-hidden="true" />
          {statusText}
          {person?.contactId != null && ` · ${t("inContacts")}`}
        </p>
      }
      footer={
        person !== null && (
          <>
            <button type="button" className="participants__button" onClick={() => void remind()}>
              {t("remindNow")}
            </button>
            <button type="button" className="participants__button" onClick={() => void toggleReminders()}>
              {person.remindersOff ? t("remindersOn") : t("remindersOff")}
            </button>
            <button type="button" className="participants__button person-panel__remove" onClick={onRemove}>
              {t("remove")}
            </button>
            <button type="button" className="participants__button participants__button--primary" onClick={save}>
              {t("save")}
            </button>
          </>
        )
      }
    >
      {person === null ? (
        <p role="status" aria-busy="true" className="participants__muted">
          …
        </p>
      ) : (
        <div className="person-panel__body">
          <div className="person-panel__form">
            <label htmlFor={`${id}-email`}>{t("email")}</label>
            <span>
              <input id={`${id}-email`} className="designer-input" type="email" value={email} aria-invalid={errors.email !== undefined} onChange={(event) => setEmail(event.target.value)} />
              {errors.email !== undefined && <span className="designer-error">{t(errors.email)}</span>}
            </span>
            <label htmlFor={`${id}-name`}>{t("name")}</label>
            <input id={`${id}-name`} className="designer-input" value={name} onChange={(event) => setName(event.target.value)} />
            <span id={`${id}-language`}>{t("language")}</span>
            <ComboBox
              labelledBy={`${id}-language`}
              options={[
                { value: "", label: t("languageNone") },
                { value: "de", label: "Deutsch" },
                { value: "en", label: "English" },
              ]}
              value={language}
              onChange={setLanguage}
              texts={{ choose: t("languageNone") }}
            />
            {fields.map(([key, value], index) => (
              <FieldRow key={key} id={`${id}-field-${index}`} label={fieldLabel(key)} value={value} onChange={(next) => setFields((current) => current.map((field, at) => (at === index ? [key, next] : field)))} />
            ))}
            <label htmlFor={`${id}-external`}>{t("externalId")}</label>
            <span>
              <input id={`${id}-external`} className="designer-input person-panel__mono" value={externalId} aria-invalid={errors.externalId !== undefined} aria-describedby={`${id}-external-hint`} onChange={(event) => setExternalId(event.target.value)} />
              <span id={`${id}-external-hint`} className="look-visually-hidden">
                {t("externalIdHint")}
              </span>
              {errors.externalId !== undefined && <span className="designer-error">{t(errors.externalId)}</span>}
            </span>
          </div>

          <p className="person-panel__label">{t("personalLink")}</p>
          <div className="person-panel__link">
            <code>{shortLink(person.link)}</code>
            <button type="button" className="participants__button participants__button--small" aria-label={copied ? t("copied") : t("copyLink")} onClick={() => void writeClipboard(person.link).then(() => setCopied(true), () => undefined)}>
              {copied ? t("copied") : t("copy")}
            </button>
          </div>

          <p className="person-panel__label">{t("history")}</p>
          <ol className="person-panel__history">
            {person.history.map((entry, index) => (
              <li key={index} data-done="">
                <span>{eventText(t, entry)}</span>
                <time dateTime={entry.at}>{when(entry.at)}</time>
              </li>
            ))}
            {person.nextReminderAt !== null && (
              <li data-next="">
                <span>{t("historyNext", { count: nextReminder })}</span>
                <time dateTime={person.nextReminderAt}>{when(person.nextReminderAt)}</time>
              </li>
            )}
          </ol>

          {notice !== null && (
            <p role="status" className="participants__notice">
              {notice}
            </p>
          )}
        </div>
      )}
    </Dialog>
  );
}

/** A field and its value. */
function FieldRow({ id, label, value, onChange }: { id: string; label: string; value: string; onChange: (value: string) => void }) {
  return (
    <>
      <label htmlFor={id}>{label}</label>
      <input id={id} className="designer-input" value={value} onChange={(event) => onChange(event.target.value)} />
    </>
  );
}
