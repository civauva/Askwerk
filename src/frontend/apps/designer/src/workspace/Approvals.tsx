// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyApproval, SurveyApprovals, SurveysClient } from "@askwerk/app-client";
import { Dialog, DialogActions, useDismiss } from "@askwerk/ui";
import { useCallback, useEffect, useId, useRef, useState } from "react";
import type { WorkspaceText } from "./SurveyBar";

/** What approving needs from the App API (AWK-28). */
export type ApprovalClient = Pick<SurveysClient, "approvals" | "approve" | "withdrawApproval">;

type Clock = { timeZone?: string | undefined; now?: (() => Date) | undefined };

/** The survey's approvals, loaded again on request (after saving, approving or withdrawing). */
export function useApprovals(client: ApprovalClient, surveyId: string) {
  const [approvals, setApprovals] = useState<SurveyApprovals | null>(null);
  const reload = useCallback(() => {
    client.approvals(surveyId).then(setApprovals, () => undefined);
  }, [client, surveyId]);

  useEffect(reload, [reload]);

  return [approvals, reload] as const;
}

/** "today 10:50", "yesterday 16:20", or the date and time, in the user's time zone. */
function when(t: WorkspaceText, at: string, { timeZone, now = () => new Date() }: Clock) {
  const locale = t("today", { time: "" }).startsWith("heute") ? "de-AT" : "en-GB";
  const day = (date: Date) => new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).format(date);
  const time = new Intl.DateTimeFormat(locale, { timeZone, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(new Date(at));
  const today = now();
  const yesterday = new Date(today.getTime() - 86_400_000);

  if (day(new Date(at)) === day(today)) {
    return t("today", { time });
  }

  if (day(new Date(at)) === day(yesterday)) {
    return t("yesterday", { time });
  }

  return `${new Intl.DateTimeFormat(locale, { timeZone, day: "numeric", month: "short" }).format(new Date(at))}, ${time}`;
}

const latestCurrent = (approvals: SurveyApprovals) => approvals.items.find((item) => item.current);

/** The Publish dialog's line (W16 D): what there is; publishing never waits for it. */
export function approvalLine(t: WorkspaceText, approvals: SurveyApprovals) {
  const current = latestCurrent(approvals);

  return current !== undefined ? t("lineApproved", { name: current.by }) : approvals.items.length > 0 ? t("lineEarlier") : t("lineNone");
}

type SlotProps = Clock & {
  t: WorkspaceText;
  approvals: SurveyApprovals | null;
  title: string;
  languages: readonly string[];
  onApprove: (note: string | null) => Promise<unknown>;
  onWithdraw: (approvalId: string) => Promise<unknown>;
};

/** Approve… in the Preview header (W16 A), or that the draft is approved, with Withdraw for one's own approval. */
export function ApprovalSlot({ t, approvals, title, languages, onApprove, onWithdraw, ...clock }: SlotProps) {
  const [approving, setApproving] = useState(false);

  if (approvals === null) {
    return null;
  }

  const mine = approvals.items.find((item) => item.current && item.mine);
  const current = mine ?? latestCurrent(approvals);

  return (
    <span className="approval-slot">
      {current !== undefined && (
        <span className="approval-slot__state">
          {mine !== undefined ? t("approvedByYou", { when: when(t, mine.at, clock) }) : t("approvedBy", { name: current.by, when: when(t, current.at, clock) })}
        </span>
      )}
      {mine !== undefined ? (
        <button type="button" className="designer-link-button" onClick={() => void onWithdraw(mine.id)}>
          {t("withdraw")}
        </button>
      ) : (
        <button type="button" className="designer-button designer-button--quiet" onClick={() => setApproving(true)}>
          {t("approve")}
        </button>
      )}
      {approving && <ApproveDialog t={t} title={title} languages={languages} onCancel={() => setApproving(false)} onApprove={(note) => onApprove(note).then(() => setApproving(false))} />}
    </span>
  );
}

function ApproveDialog({ t, title, languages, onCancel, onApprove }: { t: WorkspaceText; title: string; languages: readonly string[]; onCancel: () => void; onApprove: (note: string | null) => Promise<unknown> }) {
  const id = useId();
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  return (
    <Dialog title={t("approveTitle")} onClose={onCancel}>
      <form
        className="designer-dialog__form"
        onSubmit={(event) => {
          event.preventDefault();
          setBusy(true);
          setFailed(false);
          onApprove(note.trim() === "" ? null : note.trim()).catch(() => {
            setBusy(false);
            setFailed(true);
          });
        }}
      >
        <p>{t("approveIntro")}</p>
        <p className="approval-dialog__what">
          <b>{title}</b>
          <span>{languages.map((language) => language.toUpperCase()).join(" ")}</span>
        </p>
        <p className="designer-note">{t("approveChanged")}</p>
        <label className="aw-label" htmlFor={`${id}-note`}>
          {t("approveNote")}
        </label>
        <textarea id={`${id}-note`} className="designer-input" rows={2} maxLength={500} placeholder={t("approveNoteHint")} value={note} onChange={(event) => setNote(event.target.value)} />
        {failed && <p className="designer-error">{t("failed")}</p>}
        <DialogActions>
          <button type="button" className="designer-button designer-button--quiet" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="designer-button designer-button--primary" disabled={busy}>
            {t("approveButton")}
          </button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** The approval next to Publish (W16 C); it opens every approval of the survey, newest first, with its note. */
export function ApprovalPill({ t, approvals, ...clock }: Clock & { t: WorkspaceText; approvals: SurveyApprovals }) {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLSpanElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const current = latestCurrent(approvals);
  const label =
    current !== undefined ? t("pillApproved", { name: current.by, when: when(t, current.at, clock) }) : approvals.items.length > 0 ? t("pillEarlier") : t("pillNone");

  useDismiss(open, container, (refocus) => {
    setOpen(false);

    if (refocus) {
      button.current?.focus();
    }
  });

  return (
    <span ref={container} className="designer-menu-anchor">
      <button
        ref={button}
        type="button"
        className={`approval-pill approval-pill--${current !== undefined ? "approved" : approvals.items.length > 0 ? "earlier" : "none"}`}
        aria-expanded={open}
        disabled={approvals.items.length === 0}
        onClick={() => setOpen((shown) => !shown)}
      >
        {label}
      </button>
      {open && (
        <div className="designer-menu approval-list" role="dialog" aria-label={t("approvals")}>
          <p className="designer-published__label">{t("approvals")}</p>
          <ul>
            {approvals.items.map((item: SurveyApproval) => (
              <li key={item.id}>
                <b>{item.by}</b> <span className="designer-note">{`${when(t, item.at, clock)} · ${item.current ? t("thisDraft") : t("earlierDraft")}`}</span>
                {item.note !== null && <span className="approval-list__note">{`“${item.note}”`}</span>}
              </li>
            ))}
          </ul>
        </div>
      )}
    </span>
  );
}
