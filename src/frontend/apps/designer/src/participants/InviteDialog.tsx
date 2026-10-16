// SPDX-License-Identifier: AGPL-3.0-only
import { Dialog } from "@askwerk/ui";
import "./participants.css";
import { ApiError, type InviteRequest, type ParticipantCheck, type ParticipantsClient } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { useEffect, useId, useMemo, useState } from "react";
import { participantsMessages, type ParticipantsMessageKey } from "./messages";

export type ParticipantsT = (key: ParticipantsMessageKey, values?: Record<string, string | number>) => string;

type InviteDialogProps = {
  client: ParticipantsClient;
  surveyId: string;
  locale: Locale;
  /** The invitation as the first person would get it, from Organisation → Messages. */
  preview?: (language: string) => Promise<{ subject: string; text: string }>;
  /** The address for the links (the published view's choice); the primary one when empty. */
  host?: string;
  today?: () => Date;
  /** Pause after typing before the list is checked; tests use 0. */
  checkDelayMs?: number;
  onClose: () => void;
};

type Step =
  | { kind: "who" }
  | { kind: "message"; failed?: "no-email" | "other" }
  | { kind: "sending" }
  | { kind: "done"; text: string };

/** yyyy-MM-dd of a date, as the date field writes it. */
const isoDate = (date: Date) => date.toISOString().slice(0, 10);

/** A check in a few words: new people first, then what will not be invited and why. */
export function checkSummary(t: ParticipantsT, check: ParticipantCheck) {
  return [
    t("fresh", { count: check.participants.length }),
    ...(check.duplicates > 0 ? [t("duplicates", { count: check.duplicates })] : []),
    ...(check.alreadyInvited > 0 ? [t("alreadyInvited", { count: check.alreadyInvited })] : []),
    ...(check.unsubscribed > 0 ? [t("unsubscribed", { count: check.unsubscribed })] : []),
    ...(check.invalid.length === 1 ? [t("invalid", { line: check.invalid[0]!.line, text: check.invalid[0]!.text })] : []),
    ...(check.invalid.length > 1 ? [t("invalidMore", { count: check.invalid.length })] : []),
  ].join(" · ");
}

/**
 * Invite participants (AWK-7, W12 C), two steps: who (pasted or from a file, checked as typed) and the message
 * (now or later, the deadline, one reminder by default). While email is not set up it makes personal links instead.
 */
export function InviteDialog({ client, surveyId, locale, preview, host, today = () => new Date(), checkDelayMs = 300, onClose }: InviteDialogProps) {
  const t: ParticipantsT = useMemo(() => createTranslator(participantsMessages, locale), [locale]);
  const fileId = useId();
  const [text, setText] = useState("");
  const [check, setCheck] = useState<ParticipantCheck | null>(null);
  const [checking, setChecking] = useState(false);
  const [fileError, setFileError] = useState(false);
  const [step, setStep] = useState<Step>({ kind: "who" });
  const [later, setLater] = useState(false);
  const [sendOn, setSendOn] = useState("");
  const [deadline, setDeadline] = useState(() => isoDate(new Date(today().getTime() + 14 * 86_400_000)));
  const [reminder, setReminder] = useState(true);
  const [message, setMessage] = useState<{ subject: string; text: string } | null>(null);
  const count = check?.participants.length ?? 0;

  useEffect(() => {
    if (text.trim() === "") {
      return;
    }

    setChecking(true);
    const timer = setTimeout(() => {
      client.checkText(surveyId, text).then(
        (answer) => {
          setCheck(answer);
          setFileError(false);
          setChecking(false);
        },
        () => setChecking(false),
      );
    }, checkDelayMs);

    return () => clearTimeout(timer);
  }, [client, surveyId, text, checkDelayMs]);

  useEffect(() => {
    if (step.kind === "message" && message === null && preview !== undefined) {
      preview(locale).then(setMessage, () => undefined);
    }
  }, [step.kind, message, preview, locale]);

  const readFile = (file: File | undefined) => {
    if (file === undefined) {
      return;
    }

    setChecking(true);
    client.checkFile(surveyId, file).then(
      (answer) => {
        setCheck(answer);
        setFileError(false);
        setChecking(false);
      },
      () => {
        setFileError(true);
        setChecking(false);
      },
    );
  };

  const invite = (send: boolean) => {
    const participants = check?.participants ?? [];
    const request: InviteRequest = {
      participants,
      deadline,
      reminder,
      send,
      ...(send && later && sendOn !== "" ? { sendAt: new Date(sendOn).toISOString() } : {}),
      ...(host === undefined ? {} : { host }),
    };
    setStep({ kind: "sending" });
    client.invite(surveyId, request).then(
      (answer) =>
        setStep({
          kind: "done",
          text: !send
            ? t("linksMade", { count: answer.invited })
            : request.sendAt !== undefined
              ? t("scheduled", { count: answer.invited, when: new Date(request.sendAt).toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" }) })
              : answer.invited === 1
                ? t("sentOne")
                : t("sent", { count: answer.invited }),
        }),
      (error: unknown) => setStep({ kind: "message", failed: error instanceof ApiError && error.problem.code === "email-not-set-up" ? "no-email" : "other" }),
    );
  };

  return (
    <Dialog title={t("title")} onClose={() => step.kind !== "sending" && onClose()} wide className="invite-dialog">
        {step.kind === "who" && (
          <>
            <p className="invite-step">{t("stepWho")}</p>
            <p className="invite-hint">{t("whoLead")}</p>
            <label className="invite-label" htmlFor={`${fileId}-text`}>
              {t("addresses")}
            </label>
            <textarea id={`${fileId}-text`} rows={7} spellCheck={false} value={text} onChange={(event) => setText(event.target.value)} />
            <label className="invite-file" htmlFor={fileId}>
              {t("file")}
              <input id={fileId} type="file" accept=".csv,.xlsx,text/csv" onChange={(event) => readFile(event.target.files?.[0])} />
            </label>
            <p className="invite-summary" aria-live="polite">
              {checking ? t("checking") : fileError ? t("noEmailColumn") : check !== null ? checkSummary(t, check) : ""}
            </p>
            <p className="invite-hint">{t("language")}</p>
            <div className="invite-buttons">
              <button type="button" className="designer-button designer-button--quiet" onClick={onClose}>
                {t("cancel")}
              </button>
              <button type="button" className="designer-button invite-primary" disabled={count === 0 || checking} onClick={() => setStep({ kind: "message" })}>
                {t("next")}
              </button>
            </div>
          </>
        )}
        {(step.kind === "message" || step.kind === "sending") && (
          <>
            <p className="invite-step">{t("stepMessage")}</p>
            <p className="invite-label">{t("invitation")}</p>
            <div className="invite-preview">
              {message !== null && (
                <>
                  <p className="invite-preview__subject">{message.subject}</p>
                  <p className="invite-preview__text">{message.text.split("\n").slice(0, 4).join("\n")}</p>
                </>
              )}
              <a href="/messages" target="_blank" rel="noopener" className="designer-link-button">
                {t("editTexts")}
              </a>
              <p className="invite-hint">{t("personal")}</p>
            </div>
            <fieldset className="invite-fieldset">
              <legend>{t("send")}</legend>
              <label className="invite-radio">
                <input type="radio" name="when" checked={!later} onChange={() => setLater(false)} />
                {t("now")}
              </label>
              <label className="invite-radio">
                <input type="radio" name="when" checked={later} onChange={() => setLater(true)} />
                {t("later")}
              </label>
              {later && (
                <label className="invite-inline">
                  {t("sendOn")}
                  <input type="datetime-local" value={sendOn} onChange={(event) => setSendOn(event.target.value)} />
                </label>
              )}
            </fieldset>
            <label className="invite-inline">
              {t("answerBy")}
              <input type="date" value={deadline} aria-describedby={`${fileId}-deadline`} onChange={(event) => setDeadline(event.target.value)} />
            </label>
            <p id={`${fileId}-deadline`} className="invite-hint">
              {t("answerByHint", { deadline: "{deadline}" })}
            </p>
            <label className="invite-radio">
              <input type="checkbox" checked={reminder} onChange={(event) => setReminder(event.target.checked)} />
              {t("reminder")}
            </label>
            {step.kind === "message" && step.failed === "no-email" && <p role="alert">{t("noEmail")}</p>}
            {step.kind === "message" && step.failed === "other" && <p role="alert">{t("failed")}</p>}
            <div className="invite-buttons">
              <button type="button" className="designer-button designer-button--quiet" disabled={step.kind === "sending"} onClick={() => setStep({ kind: "who" })}>
                {t("back")}
              </button>
              {step.kind === "message" && step.failed === "no-email" ? (
                <button type="button" className="designer-button invite-primary" onClick={() => invite(false)}>
                  {count === 1 ? t("linksOne") : t("linksMany", { count })}
                </button>
              ) : (
                <button type="button" className="designer-button invite-primary" disabled={step.kind === "sending" || (later && sendOn === "")} onClick={() => invite(true)}>
                  {step.kind === "sending" ? t("sending") : count === 1 ? t("sendOne") : t("sendMany", { count })}
                </button>
              )}
            </div>
          </>
        )}
        {step.kind === "done" && (
          <>
            <p role="status" className="invite-done">
              {step.text}
            </p>
            <div className="invite-buttons">
              <button type="button" className="designer-button invite-primary" onClick={onClose}>
                {t("close")}
              </button>
            </div>
          </>
        )}
    </Dialog>
  );
}
