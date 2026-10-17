// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type ParticipantDetail, type ParticipantsClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Dialog } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { peopleMessages, type PeopleMessageKey } from "./peopleMessages";

const historyKeys: Record<ParticipantDetail["history"][number]["kind"], PeopleMessageKey> = {
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
  /** The status the list shows, e.g. "Opened, not started". */
  statusText: string;
  writeClipboard: (text: string) => Promise<void>;
  onClose: (changed: boolean) => void;
  onRemove: () => void;
};

/**
 * One person (W35): email, name, language, External ID and fields to change; the personal link; what happened and the
 * next automatic reminder; a reminder now, no more reminders, remove.
 */
export function PersonPanel({ client, surveyId, participantId, locale, timeZone, statusText, writeClipboard, onClose, onRemove }: PersonPanelProps) {
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
  const dates = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short", ...(timeZone === undefined ? {} : { timeZone }) });

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

  return (
    <Dialog title={person?.name ?? person?.email ?? t("participant")} kicker={t("participant")} onClose={() => onClose(changed)} className="person-panel">
      {person === null ? (
        <p role="status" aria-busy="true" className="participants__muted">
          …
        </p>
      ) : (
        <div className="person-panel__body">
          <p className="person-panel__status">
            {statusText}
            {person.contactId !== null && ` · ${t("inContacts")}`}
          </p>
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
            <label htmlFor={`${id}-external`}>{t("externalId")}</label>
            <span>
              <input id={`${id}-external`} className="designer-input person-panel__mono" value={externalId} aria-invalid={errors.externalId !== undefined} aria-describedby={`${id}-external-hint`} onChange={(event) => setExternalId(event.target.value)} />
              <small id={`${id}-external-hint`} className="person-panel__hint">
                {t("externalIdHint")}
              </small>
              {errors.externalId !== undefined && <span className="designer-error">{t(errors.externalId)}</span>}
            </span>
            {fields.map(([key, value], index) => (
              <FieldRow
                key={index}
                id={`${id}-field-${index}`}
                name={key}
                value={value}
                namePrompt={t("fieldName")}
                onChange={(nextKey, nextValue) => setFields((current) => current.map((field, at) => (at === index ? [nextKey, nextValue] : field)))}
              />
            ))}
            <span />
            <span>
              <button type="button" className="designer-link-button" onClick={() => setFields((current) => [...current, ["", ""]])}>
                {t("addField")}
              </button>
            </span>
          </div>

          <p className="person-panel__label">{t("personalLink")}</p>
          <div className="person-panel__link">
            <code>{person.link}</code>
            <button type="button" className="designer-button" onClick={() => void writeClipboard(person.link).then(() => setCopied(true), () => undefined)}>
              {copied ? t("copied") : t("copyLink")}
            </button>
          </div>

          <p className="person-panel__label">{t("history")}</p>
          <ol className="person-panel__history">
            {person.history.map((entry, index) => (
              <li key={index} data-done="">
                <span>{entry.kind === "reminded" ? t("historyReminded", { count: entry.count ?? 1 }) : t(historyKeys[entry.kind])}</span>
                <time dateTime={entry.at}>{dates.format(new Date(entry.at))}</time>
              </li>
            ))}
            {person.nextReminderAt !== null && (
              <li>
                <span>{t("historyNext")}</span>
                <time dateTime={person.nextReminderAt}>{dates.format(new Date(person.nextReminderAt))}</time>
              </li>
            )}
          </ol>

          {notice !== null && (
            <p role="status" className="participants__notice">
              {notice}
            </p>
          )}
          <div className="person-panel__actions">
            <button type="button" className="designer-button" onClick={() => void remind()}>
              {t("remindNow")}
            </button>
            <button type="button" className="designer-button" onClick={() => void toggleReminders()}>
              {person.remindersOff ? t("remindersOn") : t("remindersOff")}
            </button>
            <span className="participants__grow" />
            <button type="button" className="designer-button person-panel__remove" onClick={onRemove}>
              {t("remove")}
            </button>
            <button type="button" className="designer-button designer-button--primary" onClick={save}>
              {t("save")}
            </button>
          </div>
        </div>
      )}
    </Dialog>
  );
}

/** A field: its name (editable while new) and its value. */
function FieldRow({ id, name, value, namePrompt, onChange }: { id: string; name: string; value: string; namePrompt: string; onChange: (name: string, value: string) => void }) {
  const [editingName, setEditingName] = useState(name === "");

  return (
    <>
      {editingName ? (
        <input className="designer-input" aria-label={namePrompt} placeholder={namePrompt} value={name} onChange={(event) => onChange(event.target.value, value)} onBlur={() => name !== "" && setEditingName(false)} />
      ) : (
        <label htmlFor={id}>{name}</label>
      )}
      <input id={id} className="designer-input" value={value} aria-label={editingName ? `${name || namePrompt}` : undefined} onChange={(event) => onChange(name, event.target.value)} />
    </>
  );
}
