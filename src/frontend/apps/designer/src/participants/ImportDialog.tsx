// SPDX-License-Identifier: AGPL-3.0-only
import { ApiError, type ParticipantCheck, type ParticipantInput, type PeopleTable } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { ComboBox, Dialog } from "@askwerk/ui";
import { useEffect, useId, useMemo, useState } from "react";
import { peopleMessages, type PeopleMessageKey } from "./peopleMessages";

type Target = PeopleTable["guesses"][number] | "skip";

/** How the import ends (W35): the people, and for a survey whether to send now and where to keep them. */
export type ImportChoice = { readonly sendNow: boolean; readonly saveToContacts: boolean; readonly group: string; readonly fileName?: string | null };

type ImportDialogProps = {
  locale: Locale;
  /** A survey's participants, or the organisation's contacts. */
  mode: "participants" | "contacts";
  read: (source: File | string) => Promise<PeopleTable>;
  /** Participants: what the survey would do with the people. */
  check?: (people: readonly ParticipantInput[]) => Promise<ParticipantCheck>;
  /** The fields the people already have, so a new one says so. */
  knownFields?: readonly string[];
  /** Imports the people; resolves with what to tell the user. */
  onImport: (people: readonly ParticipantInput[], choice: ImportChoice) => Promise<string>;
  onClose: (notice?: string) => void;
};

const address = /^[^@\s<>",;]+@[^@\s<>",;]+\.[a-z]{2,}$/i;

/**
 * Import people (W35): a CSV or Excel file, or a table pasted from Excel; then one step with which column is what
 * (Askwerk guesses, extra columns become fields), what the check found and, for a survey, whether to send now and
 * keep them in Contacts.
 */
export function ImportDialog({ locale, mode, read, check, knownFields = [], onImport, onClose }: ImportDialogProps) {
  const t = useMemo(() => createTranslator(peopleMessages, locale), [locale]);
  const id = useId();
  const [table, setTable] = useState<PeopleTable | null>(null);
  const [targets, setTargets] = useState<Target[]>([]);
  const [pasted, setPasted] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [checked, setChecked] = useState<ParticipantCheck | null>(null);
  const [showInvalid, setShowInvalid] = useState(false);
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
        setGroup(groupOf(read.fileName));
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

  const hasEmail = targets.includes("email");

  // The check follows the mapping (W35): mapping, check and what then are one step.
  useEffect(() => {
    if (table === null || !hasEmail) {
      setChecked(null);
      return;
    }

    const everyone = people();

    if (check === undefined) {
      setChecked({ participants: everyone.filter((person) => address.test(person.email)), duplicates: 0, alreadyInvited: 0, unsubscribed: 0, invalid: everyone.flatMap((person, index) => (address.test(person.email) ? [] : [{ line: index + 2, text: person.email }])) });
      return;
    }

    let current = true;
    const timer = setTimeout(
      () =>
        check(everyone).then(
          (result) => current && setChecked(result),
          () => current && setError(t("failed")),
        ),
      150,
    );

    return () => {
      current = false;
      clearTimeout(timer);
    };
    // people() is rebuilt from the table and the targets.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [table, targets, check]);

  const submit = () => {
    if (checked === null) {
      return;
    }

    setBusy(true);
    setError(null);
    onImport(checked.participants, { sendNow, saveToContacts, group: group.trim(), fileName: table?.fileName ?? null }).then(onClose, (caught: unknown) => {
      setError(caught instanceof ApiError && caught.problem.code === "email-not-set-up" ? t("noEmail") : t("failed"));
      setBusy(false);
    });
  };

  const known = new Set(knownFields);
  const options = (column: string) => [
    { value: "email", label: t("asEmail") },
    { value: "firstName", label: t("asFirstName") },
    { value: "lastName", label: t("asLastName") },
    { value: "name", label: t("asName") },
    { value: "language", label: t("asLanguage") },
    { value: "externalId", label: t("asExternalId") },
    { value: "field", label: known.has(fieldKey(column)) ? t("asField", { name: column }) : t("asFieldNew", { name: column }) },
    { value: "skip", label: t("leaveOut") },
  ];
  const fieldExample = table?.columns.find((_, index) => targets[index] === "field");
  const steps = [t("stepFile"), t("stepColumns"), t("stepCheck"), ...(mode === "participants" ? [t("stepInvite")] : [])];
  const count = checked?.participants.length ?? 0;
  const footer =
    table === null ? (
      <>
        <button type="button" className="participants__button" onClick={() => onClose()}>
          {t("cancel")}
        </button>
        <span className="participants__grow" />
        <button type="button" className="participants__button participants__button--primary" disabled={busy || pasted.trim() === ""} onClick={() => load(pasted)}>
          {busy ? t("reading") : t("read")}
        </button>
      </>
    ) : (
      <>
        <button type="button" className="participants__button" onClick={() => (setTable(null), setChecked(null))}>
          {t("back")}
        </button>
        <span className="participants__grow" />
        {fieldExample !== undefined && <span className="participants__small">{t("placeholdersHint", { placeholder: `{${fieldKey(fieldExample)}}` })}</span>}
        <button type="button" className="participants__button participants__button--primary" disabled={busy || count === 0} onClick={submit}>
          {mode === "contacts" ? t("importContactsCount", { count }) : count === 1 ? t("importCountOne") : t("importCount", { count })}
        </button>
      </>
    );

  return (
    <Dialog
      title={mode === "contacts" ? t("importToContacts") : t("importTitle")}
      onClose={() => onClose()}
      wide
      className="import-dialog"
      head={
        <>
          <ol className="import-steps" aria-label={t("importTitle")}>
            {steps.map((label, index) => {
              const at = table === null ? 0 : 1;

              return (
                <li key={label} className={index === at ? "import-steps__on" : index < at ? "import-steps__done" : undefined} aria-current={index === at ? "step" : undefined}>
                  {index < at ? `✓ ${label}` : `${index + 1} ${label}`}
                </li>
              );
            })}
          </ol>
          <span className="participants__grow" />
          {table !== null && <span className="participants__small">{[table.fileName, t("rows", { count: table.rows.length })].filter(Boolean).join(" · ")}</span>}
        </>
      }
      footer={footer}
    >
      {table === null && (
        <div className="import-file">
          <label className="participants__button import-file__choose">
            {t("chooseFile")}
            <input type="file" accept=".csv,.xlsx,.txt,text/csv" className="look-visually-hidden" onChange={(event) => event.target.files?.[0] !== undefined && load(event.target.files[0])} />
          </label>
          <label className="aw-label" htmlFor={`${id}-paste`}>
            {t("orPaste")}
          </label>
          <textarea id={`${id}-paste`} className="designer-input import-file__paste" rows={6} value={pasted} onChange={(event) => setPasted(event.target.value)} />
        </div>
      )}

      {table !== null && (
        <div className="import-map">
          <p className="import-map__title">{t("whichColumn")}</p>
          <div className="import-map__grid" role="table" aria-label={t("whichColumn")}>
            <div role="row" className="import-map__head">
              <span role="columnheader">{t("inFile")}</span>
              <span role="columnheader" aria-hidden="true" />
              <span role="columnheader">{t("becomes")}</span>
              <span role="columnheader">{t("firstRows")}</span>
            </div>
            {table.columns.map((column, index) => (
              <div role="row" key={index} data-skip={targets[index] === "skip" ? "" : undefined}>
                <span role="cell" className="import-map__column" id={`${id}-col-${index}`}>
                  {column}
                </span>
                <span role="cell" aria-hidden="true" className="import-map__arrow">
                  →
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

          <p className="import-map__title">{t("checkFound")}</p>
          {!hasEmail ? (
            <p className="designer-error" role="alert">
              {t("needEmail")}
            </p>
          ) : (
            <div className="import-check__numbers" aria-busy={checked === null || undefined}>
              <span className="import-check__ok">
                <b>{checked?.participants.length ?? "…"}</b>
                {t("willAdd")}
              </span>
              {mode === "participants" && (
                <span className="import-check__warn">
                  <b>{checked?.alreadyInvited ?? "…"}</b>
                  {t("skippedInvited")}
                </span>
              )}
              <span className="import-check__bad">
                <b>{checked?.invalid.length ?? "…"}</b>
                <span>
                  {t("notValid")}
                  {(checked?.invalid.length ?? 0) > 0 && (
                    <>
                      {" · "}
                      <button type="button" className="import-check__show" aria-expanded={showInvalid} onClick={() => setShowInvalid(!showInvalid)}>
                        {showInvalid ? t("hide") : t("show")}
                      </button>
                    </>
                  )}
                </span>
              </span>
              {mode === "participants" && (
                <span className="import-check__warn">
                  <b>{checked?.unsubscribed ?? "…"}</b>
                  {t("skippedUnsubscribed")}
                </span>
              )}
              {(checked?.duplicates ?? 0) > 0 && (
                <span className="import-check__warn">
                  <b>{checked!.duplicates}</b>
                  {t("skippedDuplicates")}
                </span>
              )}
            </div>
          )}
          {showInvalid && checked !== null && checked.invalid.length > 0 && <p className="import-check__invalid">{checked.invalid.map((line) => line.text || "—").join(", ")}</p>}

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
              <span className="import-then__choice">
                <input id={`${id}-contacts`} type="checkbox" checked={saveToContacts} onChange={(event) => setSaveToContacts(event.target.checked)} />
                <label htmlFor={`${id}-contacts`}>
                  <b>{t("saveContacts")}</b>
                </label>
                <GroupTag id={id} t={t} value={group} onChange={setGroup} />
              </span>
            </fieldset>
          ) : (
            <span className="import-then__choice">
              <GroupTag id={id} t={t} value={group} onChange={setGroup} />
            </span>
          )}
        </div>
      )}

      {error !== null && (
        <p className="designer-error" role="alert">
          {error}
        </p>
      )}
    </Dialog>
  );
}

/** The group they go into, as a tag: "group: Customers Q4". */
function GroupTag({ id, t, value, onChange }: { id: string; t: ReturnType<typeof createTranslator<PeopleMessageKey>>; value: string; onChange: (value: string) => void }) {
  return (
    <span className="import-then__tag">
      <span aria-hidden="true">{t("groupTag")}</span>
      <input id={`${id}-group`} aria-label={t("contactGroup")} value={value} maxLength={100} size={Math.max(8, value.length + 1)} onChange={(event) => onChange(event.target.value)} />
    </span>
  );
}

/** A group named after the file: "customers-q4.csv" becomes "Customers Q4". */
export function groupOf(fileName: string | null): string {
  return (fileName ?? "")
    .replace(/\.[^.]+$/, "")
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((word) => word.charAt(0).toLocaleUpperCase() + word.slice(1))
    .join(" ");
}

/** A field's key as placeholders use it: "Customer no." becomes customer_no (as the server does). */
export function fieldKey(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, "_")
    .replace(/^_+|_+$/g, "");
}
