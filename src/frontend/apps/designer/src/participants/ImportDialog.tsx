// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type ParticipantCheck, type ParticipantInput, type PeopleTable } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Dialog, DialogActions } from "@askwerk/ui";
import { useId, useMemo, useState } from "react";
import { peopleMessages } from "./peopleMessages";

type Target = PeopleTable["guesses"][number] | "skip";

/** How the import ends (W35): the people, and for a survey whether to send now and where to keep them. */
export type ImportChoice = { readonly sendNow: boolean; readonly saveToContacts: boolean; readonly group: string };

type ImportDialogProps = {
  locale: Locale;
  /** A survey's participants, or the organisation's contacts. */
  mode: "participants" | "contacts";
  read: (source: File | string) => Promise<PeopleTable>;
  /** Participants: what the survey would do with the people. */
  check?: (people: readonly ParticipantInput[]) => Promise<ParticipantCheck>;
  /** Imports the people; resolves with what to tell the user. */
  onImport: (people: readonly ParticipantInput[], choice: ImportChoice) => Promise<string>;
  onClose: (notice?: string) => void;
};

const address = /^[^@\s<>",;]+@[^@\s<>",;]+\.[a-z]{2,}$/i;

/**
 * Import people (W35): a CSV or Excel file, or a table pasted from Excel; then which column is what (Askwerk guesses,
 * extra columns become fields); then the check and, for a survey, whether to send now and keep them in Contacts.
 */
export function ImportDialog({ locale, mode, read, check, onImport, onClose }: ImportDialogProps) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const id = useId();
  const [table, setTable] = useState<PeopleTable | null>(null);
  const [targets, setTargets] = useState<Target[]>([]);
  const [pasted, setPasted] = useState("");
  const [step, setStep] = useState<"file" | "columns" | "check">("file");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [checked, setChecked] = useState<ParticipantCheck | null>(null);
  const [sendNow, setSendNow] = useState(true);
  const [saveToContacts, setSaveToContacts] = useState(mode === "contacts");
  const [group, setGroup] = useState("");

  const load = (source: File | string) => {
    setBusy(true);
    setError(null);
    read(source).then(
      (read) => {
        setTable(read);
        setTargets([...read.guesses]);
        setStep("columns");
        setBusy(false);
      },
      (caught: unknown) => {
        const code = caught instanceof ApiError ? caught.problem.errors?.file?.[0] : undefined;
        setError(code === "file-too-large" ? t("tooLarge") : code === "no-rows" ? t("noRows") : t("failed"));
        setBusy(false);
      },
    );
  };

  const people = (): ParticipantInput[] => {
    if (table === null) {
      return [];
    }

    const at = (target: Target) => targets.indexOf(target);
    const cell = (row: readonly string[], index: number) => (index >= 0 ? (row[index] ?? "").trim() : "");

    return table.rows.map((row) => {
      const name = cell(row, at("name")) || [cell(row, at("firstName")), cell(row, at("lastName"))].filter(Boolean).join(" ");
      const fields: Record<string, string> = {};
      targets.forEach((target, index) => {
        const value = cell(row, index);
        target === "field" && value !== "" && (fields[table.columns[index]!] = value);
      });

      return { email: cell(row, at("email")), name: name || null, language: cell(row, at("language")) || null, externalId: cell(row, at("externalId")) || null, fields };
    });
  };

  const toCheck = () => {
    if (!targets.includes("email")) {
      setError(t("needEmail"));
      return;
    }

    setError(null);
    const everyone = people();

    if (check === undefined) {
      const valid = everyone.filter((person) => address.test(person.email));
      setChecked({ participants: valid, duplicates: 0, alreadyInvited: 0, unsubscribed: 0, invalid: everyone.filter((person) => !address.test(person.email)).map((person, index) => ({ line: index + 2, text: person.email })) });
      setStep("check");
      return;
    }

    setBusy(true);
    check(everyone).then(
      (result) => {
        setChecked(result);
        setStep("check");
        setBusy(false);
      },
      () => {
        setError(t("failed"));
        setBusy(false);
      },
    );
  };

  const submit = () => {
    if (checked === null) {
      return;
    }

    setBusy(true);
    setError(null);
    onImport(checked.participants, { sendNow, saveToContacts, group: group.trim() }).then(onClose, (caught: unknown) => {
      setError(caught instanceof ApiError && caught.problem.code === "email-not-set-up" ? t("noEmail") : t("failed"));
      setBusy(false);
    });
  };

  const options = (column: string) => [
    { value: "email", label: t("asEmail") },
    { value: "firstName", label: t("asFirstName") },
    { value: "lastName", label: t("asLastName") },
    { value: "name", label: t("asName") },
    { value: "language", label: t("asLanguage") },
    { value: "externalId", label: t("asExternalId") },
    { value: "field", label: t("asField", { name: column }) },
    { value: "skip", label: t("leaveOut") },
  ];
  const fieldExample = table?.columns.find((_, index) => targets[index] === "field");
  const stepClass = (mine: typeof step, order: number) => (mine === step ? "import-steps__on" : ["file", "columns", "check"].indexOf(step) > order ? "import-steps__done" : undefined);

  return (
    <Dialog title={mode === "contacts" ? t("importToContacts") : t("importTitle")} onClose={() => onClose()} wide className="import-dialog">
      <ol className="import-steps" aria-label={t("importTitle")}>
        <li className={stepClass("file", 0)}>{t("stepFile")}</li>
        <li className={stepClass("columns", 1)}>{t("stepColumns")}</li>
        <li className={stepClass("check", 2)}>{t("stepCheck")}</li>
        {table !== null && <li className="import-steps__meta">{[table.fileName, t("rows", { count: table.rows.length })].filter(Boolean).join(" · ")}</li>}
      </ol>

      {step === "file" && (
        <div className="import-file">
          <label className="designer-button">
            {t("chooseFile")}
            <input type="file" accept=".csv,.xlsx,.txt,text/csv" className="look-visually-hidden" onChange={(event) => event.target.files?.[0] !== undefined && load(event.target.files[0])} />
          </label>
          <label className="aw-label" htmlFor={`${id}-paste`}>
            {t("orPaste")}
          </label>
          <textarea id={`${id}-paste`} className="designer-input import-file__paste" rows={6} value={pasted} onChange={(event) => setPasted(event.target.value)} />
          <DialogActions>
            <button type="button" className="designer-button designer-button--quiet" onClick={() => onClose()}>
              {t("cancel")}
            </button>
            <button type="button" className="designer-button designer-button--primary" disabled={busy || pasted.trim() === ""} onClick={() => load(pasted)}>
              {busy ? t("reading") : t("read")}
            </button>
          </DialogActions>
        </div>
      )}

      {step === "columns" && table !== null && (
        <div className="import-map">
          <p className="import-map__title">{t("whichColumn")}</p>
          <div className="import-map__grid" role="table" aria-label={t("whichColumn")}>
            <div role="row" className="import-map__head">
              <span role="columnheader">{t("inFile")}</span>
              <span role="columnheader">{t("becomes")}</span>
              <span role="columnheader">{t("firstRows")}</span>
            </div>
            {table.columns.map((column, index) => (
              <div role="row" key={index}>
                <span role="cell" className="import-map__column" id={`${id}-col-${index}`}>
                  {column}
                </span>
                <span role="cell">
                  <ComboBox
                    labelledBy={`${id}-col-${index}`}
                    options={options(column)}
                    value={targets[index] ?? "skip"}
                    onChange={(value) =>
                      setTargets((current) => current.map((target, at) => (at === index ? (value as Target) : value !== "field" && value !== "skip" && target === value ? "skip" : target)))
                    }
                    texts={{ choose: t("leaveOut") }}
                  />
                </span>
                <span role="cell" className="import-map__sample">
                  {table.rows
                    .slice(0, 3)
                    .map((row) => row[index] ?? "")
                    .join(", ")}
                  , …
                </span>
              </div>
            ))}
          </div>
          {error !== null && (
            <p className="designer-error" role="alert">
              {error}
            </p>
          )}
          <DialogActions>
            <button type="button" className="designer-button designer-button--quiet" onClick={() => setStep("file")}>
              {t("back")}
            </button>
            <button type="button" className="designer-button designer-button--primary" disabled={busy} onClick={toCheck}>
              {t("next")}
            </button>
          </DialogActions>
        </div>
      )}

      {step === "check" && checked !== null && (
        <div className="import-check">
          <p className="import-map__title">{t("checkFound")}</p>
          <div className="import-check__numbers">
            <span className="import-check__ok">
              <b>{checked.participants.length}</b>
              {t("willAdd")}
            </span>
            {checked.alreadyInvited > 0 && (
              <span className="import-check__warn">
                <b>{checked.alreadyInvited}</b>
                {t("skippedInvited")}
              </span>
            )}
            {checked.duplicates > 0 && (
              <span className="import-check__warn">
                <b>{checked.duplicates}</b>
                {t("skippedDuplicates")}
              </span>
            )}
            {checked.invalid.length > 0 && (
              <span className="import-check__bad" title={checked.invalid.map((line) => line.text).join(", ")}>
                <b>{checked.invalid.length}</b>
                {t("notValid")}
              </span>
            )}
            {checked.unsubscribed > 0 && (
              <span className="import-check__warn">
                <b>{checked.unsubscribed}</b>
                {t("skippedUnsubscribed")}
              </span>
            )}
          </div>
          {mode === "participants" ? (
            <fieldset className="import-then">
              <legend className="import-map__title">{t("stepThen")}</legend>
              <label className="import-then__choice">
                <input type="radio" name={`${id}-send`} checked={sendNow} onChange={() => setSendNow(true)} />
                <span>
                  <b>{t("sendNow")}</b>
                  <small>{t("sendNowHint")}</small>
                </span>
              </label>
              <label className="import-then__choice">
                <input type="radio" name={`${id}-send`} checked={!sendNow} onChange={() => setSendNow(false)} />
                <span>
                  <b>{t("sendLater")}</b>
                  <small>{t("sendLaterHint")}</small>
                </span>
              </label>
              <label className="import-then__choice">
                <input type="checkbox" checked={saveToContacts} onChange={(event) => setSaveToContacts(event.target.checked)} />
                <span>
                  <b>{t("saveContacts")}</b>
                </span>
              </label>
              {saveToContacts && <GroupField id={id} label={t("contactGroup")} value={group} onChange={setGroup} />}
            </fieldset>
          ) : (
            <GroupField id={id} label={t("contactGroup")} value={group} onChange={setGroup} />
          )}
          {error !== null && (
            <p className="designer-error" role="alert">
              {error}
            </p>
          )}
          <DialogActions>
            <button type="button" className="designer-button designer-button--quiet" onClick={() => setStep("columns")}>
              {t("back")}
            </button>
            {fieldExample !== undefined && <span className="import-check__hint">{t("placeholdersHint", { placeholder: `{${fieldKey(fieldExample)}}` })}</span>}
            <button type="button" className="designer-button designer-button--primary" disabled={busy || checked.participants.length === 0} onClick={submit}>
              {mode === "contacts" ? t("importContactsCount", { count: checked.participants.length }) : t("importCount", { count: checked.participants.length })}
            </button>
          </DialogActions>
        </div>
      )}

      {step === "file" && error !== null && (
        <p className="designer-error" role="alert">
          {error}
        </p>
      )}
    </Dialog>
  );
}

function GroupField({ id, label, value, onChange }: { id: string; label: string; value: string; onChange: (value: string) => void }) {
  return (
    <div className="import-then__group">
      <label className="aw-label" htmlFor={`${id}-group`}>
        {label}
      </label>
      <input id={`${id}-group`} className="designer-input" value={value} maxLength={100} onChange={(event) => onChange(event.target.value)} />
    </div>
  );
}

/** A field's key as placeholders use it: "Customer no." becomes customer_no (as the server does). */
export function fieldKey(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, "_")
    .replace(/^_+|_+$/g, "");
}
