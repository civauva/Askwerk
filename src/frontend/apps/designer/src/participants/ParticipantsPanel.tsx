// SPDX-License-Identifier: AGPL-3.0-only
import "./participants.css";
import { ApiError, type ParticipantItem, type ParticipantList, type ParticipantsClient, type ParticipantSelection, type ParticipantStatus } from "@askwerk/app-client";
import { createTranslator, type Locale } from "@askwerk/schema";
import { DataGrid, dataGridTexts, pageSizes, sortToParam, type DataGridColumn, type DataGridSort } from "@askwerk/ui";
import { useCallback, useEffect, useMemo, useState } from "react";
import { browserStorage, type PreferenceStorage } from "../account/AccountApp";
import { InviteDialog } from "./InviteDialog";
import { participantsMessages, type ParticipantsMessageKey } from "./messages";

type ParticipantsPanelProps = {
  client: ParticipantsClient;
  surveyId: string;
  locale: Locale;
  preview?: (language: string) => Promise<{ subject: string; text: string }>;
  writeClipboard?: (text: string) => Promise<void>;
  timeZone?: string;
  /** Remembers the page size on this device. */
  storage?: PreferenceStorage;
};

const sizeKey = "askwerk.grid.participants.size";

const statusKeys: Record<ParticipantStatus, ParticipantsMessageKey> = {
  waiting: "waiting",
  "link-only": "link-only",
  sent: "sentStatus",
  opened: "opened",
  started: "started",
  answered: "answered",
  bounced: "bounced",
  unsubscribed: "unsubscribedStatus",
};

/** Statuses in the order the summary names them. */
const summaryOrder: readonly ParticipantStatus[] = ["answered", "started", "opened", "sent", "waiting", "link-only", "bounced", "unsubscribed"];

/**
 * Results → Participants (AWK-7, W12 D): who was invited and what happened, the next reminder, a reminder now,
 * each person's link to copy, all links as CSV, and invite more.
 */
export function ParticipantsPanel({ client, surveyId, locale, preview, writeClipboard = (text) => navigator.clipboard.writeText(text), timeZone, storage = browserStorage }: ParticipantsPanelProps) {
  const t = useMemo(() => createTranslator(participantsMessages, locale), [locale]);
  const [list, setList] = useState<ParticipantList | null>(null);
  const [failed, setFailed] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const [inviting, setInviting] = useState(false);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(() => [Number(storage.get(sizeKey))].find((value) => (pageSizes as readonly number[]).includes(value)) ?? 50);
  const [sort, setSort] = useState<DataGridSort | undefined>(undefined);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [all, setAll] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const reload = useCallback(() => {
    setFailed(false);

    return client.list(surveyId, { ...(sort === undefined ? {} : { sort: sortToParam(sort) }), offset: (page - 1) * size, limit: size }).then(setList, () => setFailed(true));
  }, [client, surveyId, sort, page, size]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const dates = new Intl.DateTimeFormat(locale, { dateStyle: "medium", ...(timeZone === undefined ? {} : { timeZone }) });

  const chosenCount = all ? (list?.total ?? 0) : selected.size;
  const chosen = (): ParticipantSelection => (all ? { all: true } : { participantIds: [...selected] });
  const clearChoice = () => {
    setSelected(new Set());
    setAll(false);
    setConfirming(false);
  };
  const failure = (error: unknown) => setNotice(error instanceof ApiError && error.problem.code === "email-not-set-up" ? t("remindNoEmail") : t("failed"));

  const remind = () => {
    setNotice(null);
    client.remind(surveyId).then(() => setNotice(t("reminded")), failure);
  };

  const remindChosen = () => {
    setNotice(null);
    const count = chosenCount;
    client.remind(surveyId, all ? undefined : chosen()).then(() => {
      setNotice(all ? t("reminded") : t("remindedChosen", { count }));
      clearChoice();
    }, failure);
  };

  const removeChosen = () => {
    setNotice(null);
    client.remove(surveyId, chosen()).then(({ removed, kept }) => {
      setNotice(kept === 0 ? t("removed", { removed }) : t("removedKept", { removed, kept }));
      clearChoice();
      void reload();
    }, failure);
  };

  const columns: DataGridColumn<ParticipantItem>[] = [
    { key: "name", header: t("name"), sortable: true, phone: "title", cell: (item) => item.name ?? "—" },
    { key: "email", header: t("email"), cell: (item) => item.email },
    { key: "status", header: t("status"), sortable: true, cell: (item) => t(statusKeys[item.status]) },
    { key: "sent", header: t("sentAt"), align: "end", sortable: true, cell: (item) => (item.sentAt === null ? "—" : dates.format(new Date(item.sentAt))) },
    { key: "reminders", header: t("reminders"), align: "end", cell: (item) => String(item.reminders) },
    {
      key: "link",
      header: t("link"),
      cell: (item) => (
        <button
          type="button"
          className="results-open participants__copy"
          aria-label={t("copyLinkOf", { email: item.email })}
          onClick={(event) => {
            event.stopPropagation();
            void writeClipboard(item.link).then(() => setCopied(item.id), () => undefined);
          }}
        >
          {copied === item.id ? t("copied") : t("copyLink")}
        </button>
      ),
    },
  ];

  const summary =
    list === null
      ? ""
      : [
          t("invited", { count: list.total }),
          ...summaryOrder.filter((status) => (list.counts[status] ?? 0) > 0).map((status) => `${list.counts[status]} ${t(statusKeys[status]).toLowerCase()}`),
          ...(list.reminderAt === null ? [] : [t("reminderPlanned", { when: dates.format(new Date(list.reminderAt)) })]),
        ].join(" · ");

  return (
    <div className="participants">
      {list !== null && (
        <div className="participants__head">
          <p className="results-card__muted">{list.total === 0 ? t("none") : summary}</p>
          <span className="participants__grow" />
          <button type="button" className="designer-button designer-button--quiet" onClick={() => setInviting(true)}>
            {t("inviteMore")}
          </button>
          {list.total > 0 && (
            <>
              <button type="button" className="designer-button designer-button--quiet" onClick={remind}>
                {t("remindNow")}
              </button>
              <a className="designer-link-button" href={client.linksUrl(surveyId)} download>
                {t("download")}
              </a>
            </>
          )}
        </div>
      )}
      {notice !== null && (
        <p role="status" className="participants__notice">
          {notice}
        </p>
      )}
      {(list === null || list.total > 0) && (
        <DataGrid
          label={t("participants")}
          columns={columns}
          rows={list?.items ?? null}
          rowKey={(item) => item.id}
          sort={sort}
          onSort={setSort}
          paging={{
            page,
            pageSize: size,
            total: list?.total ?? 0,
            onPage: setPage,
            onPageSize: (next) => {
              setSize(next);
              storage.set(sizeKey, String(next));
            },
          }}
          selection={{
            selected,
            all,
            rowLabel: (item) => item.email,
            onChange: (next, nextAll) => {
              setSelected(next);
              setAll(nextAll);
              setConfirming(false);
            },
            actions: confirming ? (
              <>
                <span>{chosenCount === 1 ? t("removeAskOne") : t("removeAsk", { count: chosenCount })}</span>
                <button type="button" onClick={removeChosen}>
                  {t("removeYes")}
                </button>
                <button type="button" onClick={() => setConfirming(false)}>
                  {t("keep")}
                </button>
              </>
            ) : (
              <>
                <button type="button" onClick={remindChosen}>
                  {t("remindChosen")}
                </button>
                <button type="button" onClick={() => setConfirming(true)}>
                  {t("remove")}
                </button>
              </>
            ),
          }}
          error={failed}
          onRetry={() => void reload()}
          texts={dataGridTexts(locale)}
        />
      )}
      {inviting && (
        <InviteDialog
          client={client}
          surveyId={surveyId}
          locale={locale}
          {...(preview === undefined ? {} : { preview })}
          onClose={() => {
            setInviting(false);
            void reload();
          }}
        />
      )}
    </div>
  );
}
